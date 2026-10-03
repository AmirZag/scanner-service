using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NJsonSchema;
using Scalar.AspNetCore;
using ScannerService.Application.DTOs;
using ScannerService.Application.Interfaces;
using ScannerService.Application.Validators;
using ScannerService.Infrastructure.Persistence;
using ScannerService.Infrastructure.Repositories;
using ScannerService.Infrastructure.Services;
using ScannerService.TrayApp.Configurations;
using Serilog;
using Serilog.Events;

namespace ScannerService.TrayApp;

public class WebApiHostService : IDisposable
{
    private WebApplication? _app;
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private readonly ScannerServiceConfiguration _config;
    private readonly LocalSettingsStore _settingsStore;

    // Resolved once per start; drives the port probe, the Kestrel bind, the beyond-loopback warning
    // and the local URL host. A field (not a local) so the start-failure catch blocks can read it.
    private ApiBindTarget _bindTarget = ParseApiHost("localhost");
    private readonly SemaphoreSlim _stopGate = new(1, 1);
    private bool _isDisposed;

    private static readonly CompositeFormat DataSourceFormat = CompositeFormat.Parse("Data Source={0}");
    private static readonly CompositeFormat WebApiStartedFormat = CompositeFormat.Parse("Web API started on {0}:{1}");
    private static readonly CompositeFormat WebApiFailedFormat = CompositeFormat.Parse("Failed to start Web API: {0}");
    private static readonly CompositeFormat WebApiStopErrorFormat = CompositeFormat.Parse("Error stopping Web API: {0}");
    private static readonly CompositeFormat ManualEsclDeviceLogFormat = CompositeFormat.Parse("{0} -> {1}");

    public bool IsRunning { get; private set; }
    public int ActualPort { get; private set; }

    /// <summary>Port configured in appsettings.json before any port-fallback search.</summary>
    public int ConfiguredPort => _config.ApiPort;

    /// <summary>
    /// Raised when an API consumer asks for the host to restart so persisted settings apply.
    /// TrayApp subscribes and rebuilds this host in-process; stale senders (an old host whose
    /// successor already serves) are dropped by the subscriber via reference equality.
    /// </summary>
    public event EventHandler? RestartRequested;

    public void RequestRestart()
    {
        RestartRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Host spliced into locally-constructed URLs (health poll, API docs): "localhost" for loopback
    /// and wildcard binds (those always cover loopback), the literal IP (bracketed when IPv6) for a
    /// specific-address bind.
    /// </summary>
    public string LocalUrlHost { get; private set; }

    public WebApiHostService(ScannerServiceConfiguration config, LocalSettingsStore settingsStore)
    {
        _config = config;
        _settingsStore = settingsStore;
        ActualPort = config.ApiPort;
        LocalUrlHost = ParseApiHost(config.ApiHost).UrlHost;
    }

    public async Task StartAsync()
    {
        if (IsRunning)
        {
            return;
        }

        try
        {
            // Logging FIRST: on host restarts the old host's Dispose has already flushed the
            // static logger silent, and the firewall step below logs the manual netsh command
            // on unelevated runs — that must not land in the silent window.
            var loggingConfig = SerilogConfigurationExtensions.LoadLoggingConfiguration();
            var logValidation = Configurations.ConfigurationValidator.ValidateLoggingConfiguration(loggingConfig);
            if (!logValidation.IsValid)
            {
                throw new InvalidOperationException("Logging configuration validation failed: " + string.Join("; ", logValidation.Errors));
            }

            SerilogConfigurationExtensions.InitializeSerilog(loggingConfig, AppContext.BaseDirectory);

            // eSCL network scanner discovery needs inbound UDP 5353 (mDNS) allowed for this executable;
            // the default firewall policy silently drops the device's multicast answers. Adding the
            // rule requires elevation; unelevated runs log the manual command instead.
            NetworkDiscoveryFirewall.EnsureRule(Program.IsRunAsAdministrator());

            _cts = new CancellationTokenSource();
            _bindTarget = ParseApiHost(_config.ApiHost);

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ContentRootPath = AppContext.BaseDirectory,
                EnvironmentName = Environments.Production
            });

            builder.Host.UseSerilog();

            var availablePort = await FindAvailablePortAsync(_config.ApiPort, _bindTarget.ProbeAddress);
            if (availablePort != _config.ApiPort)
            {
                Log.Warning("Requested port {RequestedPort} is in use, using alternative port {AvailablePort}", _config.ApiPort, availablePort);
            }
            ActualPort = availablePort;
            LocalUrlHost = _bindTarget.UrlHost;

            // Binding beyond loopback exposes the unauthenticated API to the network; make sure this
            // is a deliberate choice (loud warning) and that the firewall lets remote clients through.
            if (_bindTarget.BindsBeyondLoopback)
            {
                ApiFirewallRuleOutcome firewallOutcome = ApiListenerFirewall.EnsureRule(Program.IsRunAsAdministrator());
                Log.Warning(
                    "SECURITY: the Web API is listening beyond loopback ({BindDescription}, port {Port}) because ScannerService:ApiHost is '{ApiHost}'. "
                    + "The API has NO authentication and allows any origin, so every device that can reach this machine can read profiles and start scans. "
                    + "Windows Firewall rule status: {FirewallOutcome}; if the rule was not created and remote clients cannot connect, add an inbound TCP allow rule for this executable (run the app once as administrator, or use the netsh command logged above when a rule was attempted). "
                    + "Set ApiHost back to \"localhost\" in appsettings.json to restrict access.",
                    _bindTarget.BindDescription, ActualPort, _config.ApiHost, firewallOutcome);
            }

            builder.WebHost.ConfigureKestrel(options =>
            {
                if (_bindTarget.Kind == ApiBindKind.Loopback)
                {
                    options.ListenLocalhost(ActualPort);
                }
                else if (_bindTarget.Kind == ApiBindKind.AnyIp)
                {
                    options.ListenAnyIP(ActualPort);
                }
                else
                {
                    options.Listen(_bindTarget.Address!, ActualPort);
                }

                // Limit max request body size to 100 MB
                options.Limits.MaxRequestBodySize = 104857600; // 100 MB
            });

            var dbPath = Path.Combine(AppContext.BaseDirectory, "scanner.db");
            builder.Services.AddDbContext<Context>(options =>
                options.UseSqlite(string.Format(CultureInfo.InvariantCulture, DataSourceFormat, dbPath)));

            // Bounded budgets for scanner discovery/scanning (drivers can hang on offline devices)
            builder.Services.AddSingleton(new Domain.Common.ScannerTimeouts
            {
                DriverTimeoutMs = _config.DriverTimeoutMs,
                EsclSearchTimeoutMs = _config.EsclSearchTimeoutMs,
                EsclSearchMarginMs = _config.EsclSearchMarginMs,
                DriverCooldownMs = _config.DriverCooldownMs,
                DriverCooldownMaxMs = _config.DriverCooldownMaxMs,
                ScanQueueTimeoutMs = _config.ScanQueueTimeoutMs,
                ScanOverallTimeoutMs = _config.ScanOverallTimeoutMs,
                ScanNoProgressTimeoutMs = _config.ScanNoProgressTimeoutMs,
                ShutdownTimeoutMs = _config.ShutdownTimeoutMs
            });

            // Bound the host's graceful-shutdown drain (the 30s default lets a wedged request stall exit)
            builder.Services.Configure<HostOptions>(options =>
                options.ShutdownTimeout = TimeSpan.FromMilliseconds(_config.ShutdownTimeoutMs));

            // Hard backstop so HTTP clients always receive a status code even when a native scanner
            // call ignores cancellation; registered policies are applied per-endpoint.
            builder.Services.AddRequestTimeouts(options =>
            {
                options.AddPolicy(Domain.Common.ApplicationConstants.RequestTimeoutPolicies.Scanners,
                    TimeSpan.FromSeconds(_config.ScannersRequestTimeoutSeconds));
                options.AddPolicy(Domain.Common.ApplicationConstants.RequestTimeoutPolicies.Scan,
                    TimeSpan.FromSeconds(_config.ScanRequestTimeoutSeconds));
            });

            // Manually configured eSCL devices: address-based, so they work even when mDNS discovery
            // cannot reach the scanner (firewalled UDP 5353, VLAN segmentation, WiFi client isolation).
            var manualEsclDevices = Configurations.ConfigurationValidator.NormalizeEsclManualDevices(_config.EsclManualDevices);
            if (manualEsclDevices.Count > 0)
            {
                Log.Information("Configured {ManualEsclDeviceCount} manual eSCL device(s): {ManualEsclDevices}",
                    manualEsclDevices.Count, string.Join("; ", manualEsclDevices.Select(d => string.Format(
                        CultureInfo.InvariantCulture, ManualEsclDeviceLogFormat, d.Name, d.Address))));
            }

            builder.Services.AddSingleton<IReadOnlyList<Domain.Common.EsclManualDevice>>(manualEsclDevices);

            // Settings API collaborators: the sidecar store (base snapshot + file IO) and a
            // deliberately non-disposable restart/hostname wrapper for this host instance.
            builder.Services.AddSingleton(_settingsStore);
            builder.Services.AddSingleton<IApiHostControl>(new ApiHostControl(this));

            // Scanner services with proper lifetime management
            builder.Services.AddSingleton<Infrastructure.Services.ScannerInitializer>();
            builder.Services.AddSingleton<IScannerInitializer>(sp => sp.GetRequiredService<Infrastructure.Services.ScannerInitializer>());
            builder.Services.AddSingleton<Infrastructure.Services.ScannerService>();
            builder.Services.AddSingleton<Infrastructure.Services.CachedScannerService>(sp =>
            {
                var scannerService = sp.GetRequiredService<Infrastructure.Services.ScannerService>();
                var memoryCache = sp.GetRequiredService<IMemoryCache>();
                var logger = sp.GetRequiredService<ILogger<Infrastructure.Services.CachedScannerService>>();
                return new Infrastructure.Services.CachedScannerService(scannerService, memoryCache, logger);
            });
            builder.Services.AddSingleton<IScannerQueries>(sp => sp.GetRequiredService<Infrastructure.Services.CachedScannerService>());
            builder.Services.AddSingleton<IScannerListCache>(sp => sp.GetRequiredService<Infrastructure.Services.CachedScannerService>());
            builder.Services.AddSingleton<IScannerService>(sp => sp.GetRequiredService<Infrastructure.Services.ScannerService>());
            builder.Services.AddScoped<IProfileRepository, ProfileRepository>();
            builder.Services.AddScoped<IExportSettingRepository, ExportSettingRepository>();
            builder.Services.AddScoped<IScanJobService, ScanJobService>();
            builder.Services.AddScoped<IRecentScansService, Infrastructure.Services.RecentScansService>();
            builder.Services.AddMemoryCache();

            builder.Services.AddValidatorsFromAssemblyContaining<UpsertProfileValidator>();

            builder.Services.AddHttpClient();

            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddOpenApiDocument(config =>
            {
                var apiVersion = typeof(Program).Assembly.GetName().Version ?? new Version(1, 0, 0);
                config.DocumentName = "openapi";
                config.Title = "Scanner Service API";
                config.Version = $"v{apiVersion}";
                config.Description = "API for managing scanners, profiles, and scanning operations";
            });

            builder.Services.AddCors(options =>
                options.AddDefaultPolicy(p =>
                    p.SetIsOriginAllowed(_ => true)
                        .AllowAnyMethod()
                        .AllowAnyHeader()
                        .AllowCredentials()));

            _app = builder.Build();

            // First middleware: enforces the registered request timeout policies (408 backstop)
            _app.UseRequestTimeouts();

            // Request body size configuration
            _app.Use(async (context, next) =>
            {
                // For scan requests, we might receive larger payloads
                if (context.Request.Path.StartsWithSegments("/api/scan"))
                {
                    context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>()!.MaxRequestBodySize = Domain.Common.ApplicationConstants.FileSizes.OneHundredMegabytes;
                }
                else
                {
                    // For other endpoints, limit to 1 MB
                    context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>()!.MaxRequestBodySize = Domain.Common.ApplicationConstants.FileSizes.OneMegabyte;
                }
                await next();
            });

            // Correlation ID middleware for request tracing
            _app.UseMiddleware<Middleware.CorrelationIdMiddleware>();

            using (var scope = _app.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<Context>().Database.EnsureCreatedAsync(_cts?.Token ?? CancellationToken.None);
            }

            _app.UseOpenApi(options =>
            {
                options.Path = "/openapi/{documentName}.json";
            });

            // Configure Scalar to point to the correct OpenAPI spec
            _app.MapScalarApiReference(options =>
            {
                options
                    .WithTitle("Scanner Service API")
                    .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient)
                    .WithOpenApiRoutePattern("/openapi/{documentName}.json");
            });

            _app.UseMiddleware<Middleware.RateLimitMiddleware>(new Middleware.RateLimitOptions
            {
                MaxRequests = Domain.Common.ApplicationConstants.RateLimit.DefaultMaxRequests,
                Window = TimeSpan.FromMinutes(Domain.Common.ApplicationConstants.RateLimit.DefaultWindowMinutes)
            });
            _app.Use(async (context, next) =>
            {
                var startTime = DateTime.UtcNow;
                Log.Debug("API Request - Method: {Method}, Path: {Path}, RemoteIP: {RemoteIP}",
                    context.Request.Method, context.Request.Path, context.Connection.RemoteIpAddress);

                try
                {
                    await next();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "API request failed - Method: {Method}, Path: {Path}, Status: {StatusCode}",
                        context.Request.Method, context.Request.Path, context.Response.StatusCode);
                    throw;
                }
                finally
                {
                    var duration = DateTime.UtcNow - startTime;
                    Log.Information("API Request completed - Method: {Method}, Path: {Path}, Status: {StatusCode}, Duration: {DurationMs}ms",
                        context.Request.Method, context.Request.Path, context.Response.StatusCode, duration.TotalMilliseconds);
                }
            });

            _app.UseCors();

            // The Scalar UI shell must never be cached: its asset layout changes between package
            // versions, and a stale cached shell renders as a blank page in the browser.
            _app.Use(async (context, next) =>
            {
                if (context.Request.Path.StartsWithSegments("/scalar"))
                {
                    context.Response.Headers.CacheControl = "no-store";
                }

                await next();
            });

            _app.ConfigureAllEndpoints();

            _runTask = _app.RunAsync(_cts?.Token ?? CancellationToken.None);
            IsRunning = true;

            Log.Information("Scanner Service API started successfully listening on {BindDescription}, port {ActualPort} (requested: {RequestedPort})", _bindTarget.BindDescription, ActualPort, _config.ApiPort);
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, WebApiStartedFormat, LocalUrlHost, ActualPort));
        }
        catch (IOException ex) when (ex.InnerException is Microsoft.AspNetCore.Connections.AddressInUseException)
        {
            Log.Error(ex,
                "Cannot bind {BindDescription} on port {Port}; the address/port is already in use. Please close any other instance of the application or change ApiHost/ApiPort in appsettings.json",
                _bindTarget.BindDescription,
                ActualPort);

            throw new InvalidOperationException(
                string.Format(CultureInfo.InvariantCulture,
                    "Address {0} on port {1} is already in use. Please close other instances or change ApiHost/ApiPort in appsettings.json.",
                    _bindTarget.BindDescription,
                    ActualPort),
                ex);
        }
        catch (Exception ex) when (IsAddressNotAvailable(ex))
        {
            Log.Error(ex,
                "Cannot bind {BindDescription}: the address is not assigned to any network adapter on this machine. "
                + "Pick an address shown by \"ipconfig\" or use \"*\" to listen on all interfaces (ScannerService:ApiHost in appsettings.json)",
                _bindTarget.BindDescription);

            throw new InvalidOperationException(
                string.Format(CultureInfo.InvariantCulture,
                    "ApiHost '{0}' is not assigned to any network adapter on this machine. Use an address shown by ipconfig or \"*\" in appsettings.json.",
                    _config.ApiHost),
                ex);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start Web API binding {BindDescription} (port {ActualPort})", _bindTarget.BindDescription, ActualPort);
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, WebApiFailedFormat, ex.Message));
            await StopAsync();
            throw;
        }
    }

    public async Task StopAsync()
    {
        if (!IsRunning)
        {
            return;
        }

        // Reentrancy-safe: concurrent stop attempts (e.g. menu + dispose racing) are ignored.
#pragma warning disable S8949 // Deliberately token-free: the shutdown CTS is already cancelled here and must not void the bound
        if (!await _stopGate.WaitAsync(TimeSpan.Zero))
        {
            return;
        }

        try
        {
            Log.Information("Stopping Scanner Service API");

            _cts?.CancelAsync();

            if (_runTask != null)
            {
                // Bound the wait: a wedged in-flight request must not block shutdown indefinitely.
                Task winner = await Task.WhenAny(_runTask, Task.Delay(_config.ShutdownTimeoutMs)).ConfigureAwait(false);
#pragma warning restore S8949
                if (winner == _runTask)
                {
                    await _runTask.ConfigureAwait(false);
                }
                else
                {
                    Log.Error("Web API host did not stop within {TimeoutMs}ms; continuing shutdown", _config.ShutdownTimeoutMs);
                }
            }

            // Clean up temporary files before disposing the app
            if (_app != null)
            {
                using var scope = _app.Services.CreateScope();
                var scanJobService = scope.ServiceProvider.GetService<IScanJobService>() as ScanJobService;
                scanJobService?.CleanupOldTempFiles(TimeSpan.Zero);
            }

            if (_app != null)
            {
                await _app.DisposeAsync().ConfigureAwait(false);
                _app = null;
            }

            IsRunning = false;
            Log.Information("Scanner Service API stopped successfully");
            Debug.WriteLine("Web API stopped");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error stopping Web API");
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, WebApiStopErrorFormat, ex.Message));
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            _runTask = null;
            _stopGate.Release();
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        // Bound the whole stop: container dispose includes scanner worker teardown, which can block on
        // a hung driver even after the host itself has stopped. StopAsync swallows its own exceptions.
#pragma warning disable S8949 // Deliberately token-free: there is no valid ambient token during final dispose
        var stopTask = Task.Run(StopAsync);
        int shutdownBoundMs = _config.ShutdownTimeoutMs * 3;
        var winner = Task.WhenAny(stopTask, Task.Delay(shutdownBoundMs)).GetAwaiter().GetResult();
#pragma warning restore S8949
        if (winner != stopTask)
        {
            Log.Error("Web API stop did not complete within {TimeoutMs}ms; continuing shutdown", shutdownBoundMs);
        }

        Log.CloseAndFlush();
        GC.SuppressFinalize(this);
    }

    private static Task<int> FindAvailablePortAsync(int startPort, IPAddress bindAddress)
    {
        // Get actively used TCP ports to avoid checking them
        var usedPorts = new HashSet<int>();

        try
        {
            var tcpConnections = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
            foreach (var listener in tcpConnections)
            {
                if (IsConflictingListener(listener, bindAddress))
                {
                    usedPorts.Add(listener.Port);
                }
            }
        }
        catch
        {
            // Fall back to checking ports directly if IPGlobalProperties fails
        }

        // Try the requested port first
        if (!usedPorts.Contains(startPort) && IsPortAvailable(startPort, bindAddress))
        {
            return Task.FromResult(startPort);
        }

        // Try ports in the range (startPort to startPort + MaxPortSearchRange)
        var maxSearch = startPort + Domain.Common.ApplicationConstants.Ports.MaxPortSearchRange;
        for (int port = startPort + 1; port <= maxSearch; port++)
        {
            if (!usedPorts.Contains(port) && IsPortAvailable(port, bindAddress))
            {
                return Task.FromResult(port);
            }
        }

        // If no port found in that range, try any available port
        using var tempListener = new System.Net.Sockets.TcpListener(bindAddress, 0);
        tempListener.Start();
        var availablePort = ((IPEndPoint)tempListener.LocalEndpoint).Port;
        tempListener.Stop();
        return Task.FromResult(availablePort);
    }

    private static bool IsPortAvailable(int port, IPAddress bindAddress)
    {
        try
        {
            using var listener = new System.Net.Sockets.TcpListener(bindAddress, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// True when an existing listener blocks binding <paramref name="bindAddress"/> on its port: a
    /// wildcard listener occupies the port for every address, a listener on the bind address
    /// conflicts directly, and a wildcard bind conflicts with any existing listener. For the
    /// loopback bind this reduces to "loopback or wildcard", matching the historical behavior.
    /// </summary>
    private static bool IsConflictingListener(System.Net.IPEndPoint listener, IPAddress bindAddress)
    {
        return listener.Address.Equals(IPAddress.Any)
            || listener.Address.Equals(bindAddress)
            || bindAddress.Equals(IPAddress.Any);
    }

    /// <summary>
    /// True when the exception chain contains a SocketException with SocketError.AddressNotAvailable
    /// (the configured address is not assigned to any network adapter). Kestrel surfaces bind
    /// failures as an IOException wrapping the socket error, while the port probe fails with a raw
    /// SocketException - both shapes must be recognized.
    /// </summary>
    private static bool IsAddressNotAvailable(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is System.Net.Sockets.SocketException socketException
                && socketException.SocketErrorCode == System.Net.Sockets.SocketError.AddressNotAvailable)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Kind of address the Web API binds to, resolved from ScannerService:ApiHost.</summary>
    private enum ApiBindKind
    {
        /// <summary>Loopback only (127.0.0.1 and [::1]).</summary>
        Loopback,
        /// <summary>All network interfaces (wildcard value).</summary>
        AnyIp,
        /// <summary>One specific IP address literal.</summary>
        SpecificAddress
    }

    /// <summary>
    /// The resolved bind decision: which kind of address to bind, the address itself, the host
    /// string for locally-constructed URLs, and a human-readable description for logs and errors.
    /// </summary>
    /// <param name="Kind">The kind of bind.</param>
    /// <param name="Address">The specific IP address; only set for <see cref="ApiBindKind.SpecificAddress"/>.</param>
    /// <param name="UrlHost">Host for locally-constructed URLs: "localhost" for loopback and wildcard binds (those always cover loopback), otherwise the IP literal (bracketed when IPv6).</param>
    /// <param name="BindDescription">Human-readable description of the bind for logs and error messages.</param>
    private sealed record ApiBindTarget(ApiBindKind Kind, IPAddress? Address, string UrlHost, string BindDescription)
    {
        /// <summary>Address the port-availability probe must bind: loopback for the default, the wildcard for all-interfaces, the literal otherwise.</summary>
        public IPAddress ProbeAddress => Kind switch
        {
            ApiBindKind.Loopback => IPAddress.Loopback,
            ApiBindKind.AnyIp => IPAddress.Any,
            _ => Address ?? IPAddress.Loopback
        };

        /// <summary>True when the bind reaches beyond loopback and exposes the (unauthenticated) API to the network.</summary>
        public bool BindsBeyondLoopback => Kind == ApiBindKind.AnyIp
            || Kind == ApiBindKind.SpecificAddress && Address is not null && !IPAddress.IsLoopback(Address);
    }

    /// <summary>
    /// Resolves the ScannerService:ApiHost value into a bind target. Values approved by the
    /// configuration validator always parse here; anything unknown falls back to the loopback
    /// target so an unvalidated or stale config can never crash host construction.
    /// </summary>
    private static ApiBindTarget ParseApiHost(string? apiHost)
    {
        string candidate = (apiHost ?? string.Empty).Trim();

        if (candidate.Length == 0
            || string.Equals(candidate, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate, "loopback", StringComparison.OrdinalIgnoreCase))
        {
            return new ApiBindTarget(ApiBindKind.Loopback, null, "localhost", "localhost (127.0.0.1, [::1])");
        }

        if (string.Equals(candidate, "*", StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate, "+", StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate, "any", StringComparison.OrdinalIgnoreCase)
            || candidate == "0.0.0.0"
            || candidate == "::")
        {
            return new ApiBindTarget(ApiBindKind.AnyIp, null, "localhost", "all interfaces (*)");
        }

        // The parse must round-trip the exact text so abbreviated forms ("0" -> 0.0.0.0,
        // "192.168.1" -> 192.168.0.1) can never silently bind a different address than typed.
        if (IPAddress.TryParse(candidate, out IPAddress? address)
            && string.Equals(address.ToString(), candidate, StringComparison.OrdinalIgnoreCase))
        {
            string urlHost = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
                ? "[" + address + "]"
                : address.ToString();
            return new ApiBindTarget(ApiBindKind.SpecificAddress, address, urlHost, "IP address " + urlHost);
        }

        // Defensive fallback: the configuration validator rejects DNS names and other invalid
        // values before the host is constructed, so this only triggers for unvalidated callers.
        return new ApiBindTarget(ApiBindKind.Loopback, null, "localhost", "localhost (127.0.0.1, [::1])");
    }

    /// <summary>
    /// Helper class to validate CORS origins for local network access.
    /// Allows localhost, 127.0.0.1, and private IP ranges (10.x.x.x, 172.16-31.x.x, 192.168.x.x).
    /// </summary>
    private static class OriginChecker
    {
        public static bool IsLocalOrigin(string? origin)
        {
            if (string.IsNullOrEmpty(origin))
            {
                return false;
            }

            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
            {
                return false;
            }

            var host = uri.Host.ToLowerInvariant();

            // Allow localhost variants
            if (host == "localhost" || host == "127.0.0.1")
            {
                return true;
            }

            // Allow any 127.x.x.x (loopback range)
            if (host.StartsWith("127.", StringComparison.Ordinal))
            {
                return true;
            }

            // Parse IP address and check if private
            if (IPAddress.TryParse(host, out var ipAddress))
            {
                var bytes = ipAddress.GetAddressBytes();

                // 10.0.0.0 - 10.255.255.255 (Class A private)
                if (bytes[0] == 10)
                {
                    return true;
                }

                // 172.16.0.0 - 172.31.255.255 (Class B private)
                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                {
                    return true;
                }

                // 192.168.0.0 - 192.168.255.255 (Class C private)
                if (bytes[0] == 192 && bytes[1] == 168)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
