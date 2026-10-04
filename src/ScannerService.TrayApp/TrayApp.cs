using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using ScannerService.Application.Common;
using ScannerService.Application.DTOs;
using ScannerService.TrayApp.Configurations;
using ScannerService.TrayApp.Properties;
using ScannerService.TrayApp;
using Serilog;
using Serilog.Events;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ScannerService.TrayApp;

public class TrayApp : ApplicationContext
{
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private readonly NotifyIcon _icon;
    private readonly System.Windows.Forms.Timer _statusCheckTimer;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ServiceProvider _httpClientServiceProvider;
    private readonly SynchronizationContext _syncContext;
    private readonly LocalSettingsStore _settingsStore;

    // The API host and its configuration are rebuilt in place when persisted settings apply
    // (RestartHostAsync), so unlike the other collaborators they are not readonly.
    private WebApiHostService _webApiHost;

    private ToolStripMenuItem? _statusItem;
    private ToolStripMenuItem? _startMenuItem;
    private ToolStripMenuItem? _stopMenuItem;
    private ToolStripMenuItem? _apiDocsMenuItem;

    private bool _isDisposed;
    private bool _isActuallyRunning;
    private readonly object _stateLock = new object();
    private readonly bool _isAdmin;

    // 1 while RestartHostAsync is rebuilding the API host; guards against concurrent restarts
    // (each successful PUT schedules one, but only the first rebuilds) and no-ops tray start/stop.
    private int _isRestarting;

    // Upper bound for the restart loop that catches settings written mid-restart.
    private const int MaxRestartPasses = 3;

    private ScannerServiceConfiguration _config;
    private string _apiHealthUrl;

    private static readonly CompositeFormat StatusRunningFormat = CompositeFormat.Parse(Resources.StatusRunningPersianFormat);
#pragma warning disable S5332 // Using http protocol is insecure - deliberate: the local Web API serves plain http by design and the host comes from config at runtime
    private static readonly CompositeFormat ApiUrlFormat = CompositeFormat.Parse("http://{0}:{1}/api/health");
    private static readonly CompositeFormat ScalarUrlFormat = CompositeFormat.Parse("http://{0}:{1}/scalar/openapi");
#pragma warning restore S5332
    private static readonly CompositeFormat FailedToOpenApiDocsFormat = CompositeFormat.Parse(Resources.FailedToOpenApiDocsFormat);
    private static readonly CompositeFormat FailedToStartServiceFormat = CompositeFormat.Parse(Resources.FailedToStartServiceFormat);
    private static readonly CompositeFormat FailedToStopServiceFormat = CompositeFormat.Parse(Resources.FailedToStopServiceFormat);
    private static readonly CompositeFormat StatusCheckFailedFormat = CompositeFormat.Parse(Resources.StatusCheckFailedFormat);
    private static readonly CompositeFormat UiUpdateFailedFormat = CompositeFormat.Parse(Resources.UiUpdateFailedFormat);
    private static readonly CompositeFormat HealthCheckFailedFormat = CompositeFormat.Parse(Resources.HealthCheckFailedFormat);
    private static readonly CompositeFormat NotificationFailedFormat = CompositeFormat.Parse(Resources.NotificationFailedFormat);

    public TrayApp(bool isAdmin)
    {
        _isAdmin = isAdmin;

        InitializeLogging();
        Log.Information("Scanner Service Tray Application starting - Running with Admin: {IsAdmin}", isAdmin);

        ScannerServiceConfiguration baseConfig = LocalSettingsStore.LoadBaseConfiguration();

        // Validate the installer-owned base configuration; a broken base is fatal (unchanged behavior).
        var baseValidation = Configurations.ConfigurationValidator.ValidateScannerServiceConfiguration(baseConfig);
        if (!baseValidation.IsValid)
        {
            var errorMessage = "Configuration validation failed:\n" + string.Join("\n", baseValidation.Errors);
            Log.Fatal("Configuration validation failed: {Errors}", string.Join("; ", baseValidation.Errors));
            MessageBox.Show(
                errorMessage + "\n\nPlease correct appsettings.json and restart the application.",
                "Configuration Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            System.Windows.Forms.Application.Exit();
            throw new InvalidOperationException("Configuration validation failed: " + string.Join("; ", baseValidation.Errors));
        }

        _settingsStore = new LocalSettingsStore(baseConfig);
        _config = ResolveStartupConfig(_settingsStore);

        _syncContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        // Create HttpClientFactory for proper HttpClient lifecycle management
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddHttpClient("StatusCheck", client =>
        {
            client.Timeout = TimeSpan.FromMilliseconds(_config.HttpTimeout);
        });
        _httpClientServiceProvider = serviceCollection.BuildServiceProvider();
        _httpClientFactory = _httpClientServiceProvider.GetService<IHttpClientFactory>()!;

        _webApiHost = new WebApiHostService(_config, _settingsStore);
        _webApiHost.RestartRequested += OnApiHostRestartRequested;

        // Note: Actual host/port will be updated when WebApiHostService starts
        _apiHealthUrl = string.Format(CultureInfo.InvariantCulture, ApiUrlFormat, _webApiHost.LocalUrlHost, _config.ApiPort);

        _icon = new NotifyIcon
        {
            Icon = CreateIcon(false),
            Visible = true,
            Text = Resources.ServiceStoppedText,
            ContextMenuStrip = CreateMenu()
        };

        _statusCheckTimer = new System.Windows.Forms.Timer
        {
            Interval = _config.StatusCheckInterval
        };
        _statusCheckTimer.Tick += OnStatusCheckTimerTick;
        _statusCheckTimer.Start();

        StartService();
    }

    /// <summary>
    /// (Re-)initializes the shared Serilog logger. Called at startup and again on the host
    /// restart failure path: disposing the old host flushes the static logger into silence,
    /// which must be undone before the failure is logged.
    /// </summary>
    private void InitializeLogging()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();

        var loggingConfig = configuration.GetSection("Logging")
            .Get<LoggingConfiguration>() ?? new LoggingConfiguration();

        SerilogConfigurationExtensions.InitializeSerilog(loggingConfig, AppContext.BaseDirectory);
    }

    /// <summary>
    /// Applies the sidecar overrides on top of the base configuration for this process start.
    /// A corrupt or invalid sidecar never prevents startup: it is reported loudly and the base
    /// values are used (the store quarantines corrupt content itself; transient read failures
    /// keep the file on disk so a later start can apply it).
    /// </summary>
    private static ScannerServiceConfiguration ResolveStartupConfig(LocalSettingsStore store)
    {
        Result<ScannerSettingsOverridesDto> overrides = store.ReadOverrides();
        if (overrides.IsFailure)
        {
            Log.Warning("Ignoring unreadable settings sidecar; starting with appsettings.json values: {Error}", overrides.Error);
            return store.BaseConfig;
        }

        ScannerServiceConfiguration merged = ScannerSettingsMapper.Merge(store.BaseConfig, overrides.Value!);
        var validation = Configurations.ConfigurationValidator.ValidateScannerServiceConfiguration(merged);
        if (!validation.IsValid)
        {
            Log.Warning("Settings sidecar values failed validation and are ignored for this start: {Errors}", string.Join("; ", validation.Errors));
            return store.BaseConfig;
        }

        return merged;
    }

    protected override void Dispose(bool disposing)
    {
        if (_isDisposed)
        {
            return;
        }
        if (disposing)
        {
            _isDisposed = true;

            Log.Information("Scanner Service Tray Application shutting down");

            if (_statusCheckTimer != null)
            {
                _statusCheckTimer.Stop();
                _statusCheckTimer.Tick -= OnStatusCheckTimerTick;
                _statusCheckTimer.Dispose();
            }

            _webApiHost?.RestartRequested -= OnApiHostRestartRequested;
            _webApiHost?.Dispose();
            _httpClientServiceProvider?.Dispose();

            if (_icon != null)
            {
                _icon.Visible = false;
                var currentIcon = _icon.Icon;
                _icon.Icon = null;
                currentIcon?.Dispose();
                _icon.Dispose();
            }

            Log.CloseAndFlush();
        }

        base.Dispose(disposing);
    }

    private ContextMenuStrip CreateMenu()
    {
        var menu = new ContextMenuStrip();

        _statusItem = new ToolStripMenuItem
        {
            Text = Resources.StatusStoppedPersian,
            Enabled = false,
            Font = new Font(menu.Font, FontStyle.Bold),
            ForeColor = Color.Red
        };
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());

        _startMenuItem = new ToolStripMenuItem(Resources.StartMenuItemText, null, (s, e) => StartService())
        {
            Name = "start",
            Enabled = true
        };
        _stopMenuItem = new ToolStripMenuItem(Resources.StopMenuItemText, null, (s, e) => StopService())
        {
            Name = "stop",
            Enabled = false
        };

        menu.Items.Add(_startMenuItem);
        menu.Items.Add(_stopMenuItem);
        menu.Items.Add(new ToolStripSeparator());

        _apiDocsMenuItem = new ToolStripMenuItem(Resources.ApiInterfaceText, null, (s, e) => OpenApiDocs())
        {
            Enabled = false
        };
        menu.Items.Add(_apiDocsMenuItem);
        menu.Items.Add(new ToolStripSeparator());

        // Add "Restart as Admin" option if not already running as admin
        if (!_isAdmin)
        {
            menu.Items.Add(new ToolStripMenuItem("اجرا با دسترسی ادمین", null, OnRestartAsAdmin)
            {
                Name = "restartAdmin"
            });
            menu.Items.Add(new ToolStripSeparator());
        }

        menu.Items.Add(new ToolStripMenuItem(Resources.ExitMenuItemText, null, OnExit));

        menu.Opening += OnMenuOpening;

        return menu;
    }

    private void OpenApiDocs()
    {
        bool running;
        lock (_stateLock)
        {
            running = _isActuallyRunning;
        }

        if (!running)
        {
            ShowNotification(Resources.PleaseStartServiceText, ToolTipIcon.Warning);
            return;
        }

        try
        {
            var url = string.Format(CultureInfo.InvariantCulture, ScalarUrlFormat, _webApiHost.LocalUrlHost, _webApiHost.ActualPort);
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open API documentation");
            MessageBox.Show(
                string.Format(CultureInfo.CurrentCulture, FailedToOpenApiDocsFormat, ex.Message),
                Resources.ErrorTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void OnMenuOpening(object? sender, EventArgs e)
    {
        bool running;
        lock (_stateLock)
        {
            running = _isActuallyRunning;
        }

        _startMenuItem?.Enabled = !running;
        _stopMenuItem?.Enabled = running;
        _apiDocsMenuItem?.Enabled = running;
    }

    private void OnStatusCheckTimerTick(object? sender, EventArgs e)
    {
        _ = CheckStatusAsync();
    }

    private async Task CheckStatusAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        try
        {
            bool isResponding = await IsApiRespondingAsync();

            lock (_stateLock)
            {
                _isActuallyRunning = isResponding;
            }

            _syncContext.Post(_ =>
            {
                if (!_isDisposed)
                {
                    UpdateUI(isResponding);
                }
            }, null);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, StatusCheckFailedFormat, ex.Message));
        }
    }

    private void UpdateUI(bool running)
    {
        try
        {
            if (_statusItem != null)
            {
                _statusItem.Text = running
                    ? string.Format(CultureInfo.CurrentCulture, StatusRunningFormat, _webApiHost.LocalUrlHost, _webApiHost.ActualPort)
                    : Resources.StatusInactivePersian;
                _statusItem.ForeColor = running ? Color.Green : Color.Red;
            }

            _startMenuItem?.Enabled = !running;
            _stopMenuItem?.Enabled = running;
            _apiDocsMenuItem?.Enabled = running;

            if (!_isDisposed && _icon != null)
            {
                var oldIcon = _icon.Icon;
                _icon.Icon = CreateIcon(running);
                _icon.Text = running ? Resources.ServiceStartedPersian : Resources.ServiceStoppedPersian;
                oldIcon?.Dispose();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, UiUpdateFailedFormat, ex.Message));
        }
    }

    private void StartService()
    {
        if (_isDisposed)
        {
            return;
        }

        // A restart rebuilds this very host; starting it concurrently would race the swap.
        if (Interlocked.CompareExchange(ref _isRestarting, 0, 0) == 1)
        {
            return;
        }

        Task.Run(async () =>
        {
            // Re-check inside the body: the outer check is advisory, and a restart scheduled
            // between the check and this body must win the race (the body would otherwise touch
            // a host that the restart is swapping out).
            if (Interlocked.CompareExchange(ref _isRestarting, 0, 0) == 1)
            {
                return;
            }

            try
            {
                if (_webApiHost.IsRunning)
                {
                    _syncContext.Post(_ =>
                    {
                        ShowNotification(Resources.AlreadyRunningText, ToolTipIcon.Info);
                    }, null);
                    return;
                }

                await _webApiHost.StartAsync();

                // Update the API health URL with the actual port used
                _syncContext.Post(_ =>
                {
                    _apiHealthUrl = string.Format(CultureInfo.InvariantCulture, ApiUrlFormat, _webApiHost.LocalUrlHost, _webApiHost.ActualPort);
                    ShowNotification(Resources.ServiceStartedText, ToolTipIcon.Info);
                }, null);

                await Task.Delay(_config.StartupDelay);
                await CheckStatusAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to start service");
                _syncContext.Post(_ =>
                {
                    MessageBox.Show(
                        string.Format(CultureInfo.CurrentCulture, FailedToStartServiceFormat, ex.Message),
                        Resources.ErrorTitle,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);

                    UpdateUI(false);
                }, null);
            }
        });
    }

    private void StopService()
    {
        if (_isDisposed)
        {
            return;
        }

        // A restart rebuilds this very host; stopping it concurrently would race the swap.
        if (Interlocked.CompareExchange(ref _isRestarting, 0, 0) == 1)
        {
            return;
        }

        Task.Run(async () =>
        {
            // Re-check inside the body: see StartService.
            if (Interlocked.CompareExchange(ref _isRestarting, 0, 0) == 1)
            {
                return;
            }

            try
            {
                await _webApiHost.StopAsync();

                lock (_stateLock)
                {
                    _isActuallyRunning = false;
                }

                _syncContext.Post(_ =>
                {
                    UpdateUI(false);
                    ShowNotification(Resources.ServiceStoppedNotificationText, ToolTipIcon.Info);
                }, null);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to stop service");
                _syncContext.Post(_ =>
                {
                    MessageBox.Show(
                        string.Format(CultureInfo.CurrentCulture, FailedToStopServiceFormat, ex.Message),
                        Resources.ErrorTitle,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }, null);
            }
        });
    }

    /// <summary>
    /// Event-handler entry point for a settings-triggered host restart. Stale raises (an old
    /// host whose successor already serves, or a raise during teardown) are dropped.
    /// </summary>
    private void OnApiHostRestartRequested(object? sender, EventArgs e)
    {
        if (_isDisposed || !ReferenceEquals(sender, _webApiHost))
        {
            return;
        }

        _ = RestartHostAsync();
    }

    /// <summary>
    /// Rebuilds the API host in place so persisted settings take effect. The new configuration
    /// is loaded and validated BEFORE the old host is touched: invalid settings never take a
    /// working host down. The bounded sequence is stop → dispose → construct → start; the new
    /// host's StartAsync re-initializes Serilog (the old host's Dispose flushes it silent).
    /// A PUT whose response completes DURING a restart has its restart raise die with the old
    /// host, so the sidecar snapshot is re-checked after each swap and the restart runs once
    /// more when it changed (bounded, so a pathological writer cannot loop forever).
    /// </summary>
    private async Task RestartHostAsync()
    {
        if (Interlocked.CompareExchange(ref _isRestarting, 1, 0) != 0)
        {
            return;
        }

        try
        {
            for (int attempt = 1; attempt <= MaxRestartPasses; attempt++)
            {
                string? sidecarBefore = _settingsStore.ReadSidecarSnapshot();
                Result<ScannerServiceConfiguration> effective = _settingsStore.LoadEffectiveConfiguration();
                if (effective.IsFailure)
                {
                    Log.Error("Not restarting the API host; the persisted settings are invalid: {Error}", effective.Error);
                    NotifySettingsRejected();
                    return;
                }

                Log.Information("Restarting the API host to apply settings");
                WebApiHostService oldHost = _webApiHost;
                oldHost.RestartRequested -= OnApiHostRestartRequested;
                ScannerServiceConfiguration previousConfig = _config;

                await oldHost.StopAsync();
                oldHost.Dispose();

                try
                {
                    var newHost = new WebApiHostService(effective.Value!, _settingsStore);
                    newHost.RestartRequested += OnApiHostRestartRequested;
                    _webApiHost = newHost;
                    _config = effective.Value!;
                    await newHost.StartAsync();
                }
                catch (Exception startFailure)
                {
                    // The old host is already gone and flushed the logger: re-init BEFORE logging.
                    InitializeLogging();
                    Log.Error(startFailure, "The API host failed to start with the new settings; rolling back to the previous configuration");
                    if (await RollbackApiHostAsync(previousConfig))
                    {
                        // Rollback may land on a different fallback port than the previous host:
                        // refresh the health URL, and tell the user the PUT was NOT applied.
                        PostRestartUiUpdate(Resources.SettingsInvalidRevertedText, ToolTipIcon.Warning);
                    }

                    return;
                }

                PostRestartUiUpdate(Resources.SettingsAppliedText, ToolTipIcon.Info);
                string? sidecarAfter = _settingsStore.ReadSidecarSnapshot();
                if (string.Equals(sidecarBefore, sidecarAfter, StringComparison.Ordinal))
                {
                    return;
                }

                Log.Information("Settings changed while the restart was in flight; applying once more");
            }
        }
        finally
        {
            Interlocked.Exchange(ref _isRestarting, 0);
        }
    }

    /// <summary>One rollback attempt with the configuration the previous host ran with;
    /// true when the rolled-back host is serving again.</summary>
    private async Task<bool> RollbackApiHostAsync(ScannerServiceConfiguration previousConfig)
    {
        try
        {
            WebApiHostService failedHost = _webApiHost;
            failedHost.RestartRequested -= OnApiHostRestartRequested;

            var rollbackHost = new WebApiHostService(previousConfig, _settingsStore);
            rollbackHost.RestartRequested += OnApiHostRestartRequested;
            _webApiHost = rollbackHost;
            _config = previousConfig;
            await rollbackHost.StartAsync();
            return true;
        }
        catch (Exception rollbackFailure)
        {
            Log.Error(rollbackFailure, "Rolling the API host back also failed; use the tray menu to start the service");
            _syncContext.Post(_ =>
            {
                if (!_isDisposed)
                {
                    UpdateUI(false);
                }
            }, null);
            return false;
        }
    }

    /// <summary>
    /// Applies tray-level state that took effect with the (re)started host — timer interval,
    /// health URL (the port-fallback search may have moved the bind) — and posts the outcome
    /// balloon. Always marshalled to the UI thread; the timer is a WinForms component.
    /// </summary>
    private void PostRestartUiUpdate(string notificationText, ToolTipIcon icon)
    {
        _syncContext.Post(_ =>
        {
            if (_isDisposed)
            {
                return;
            }

            _statusCheckTimer.Interval = _config.StatusCheckInterval;
            _apiHealthUrl = string.Format(CultureInfo.InvariantCulture, ApiUrlFormat, _webApiHost.LocalUrlHost, _webApiHost.ActualPort);
            ShowNotification(notificationText, icon);
        }, null);
    }

    private void NotifySettingsRejected()
    {
        _syncContext.Post(_ =>
        {
            if (!_isDisposed)
            {
                ShowNotification(Resources.SettingsInvalidRevertedText, ToolTipIcon.Warning);
            }
        }, null);
    }

    private async Task<bool> IsApiRespondingAsync()
    {
        try
        {
            var httpClient = _httpClientFactory.CreateClient("StatusCheck");
            httpClient.Timeout = TimeSpan.FromMilliseconds(_config.HttpTimeout);
            var response = await httpClient.GetAsync(_apiHealthUrl);
            return response.IsSuccessStatusCode;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, HealthCheckFailedFormat, ex.Message));
            return false;
        }
    }

    private Icon CreateIcon(bool running)
    {
        using var bmp = new Bitmap(16, 16);

        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            using var pen = new Pen(Color.Black, 1);
            using var statusBrush = new SolidBrush(running ? Color.LimeGreen : Color.Red);
            g.DrawRectangle(pen, 2, 2, 12, 12);

            if (running)
            {
                g.FillRectangle(statusBrush, 3, 7, 10, 2);
            }
            else
            {
                g.FillRectangle(statusBrush, 5, 5, 6, 6);
            }
        }

        // Get the icon handle and clone it to create a properly managed icon
        var handle = bmp.GetHicon();
        try
        {
            using var iconFromHandle = Icon.FromHandle(handle);
            // Clone to create a completely independent icon
            return (Icon)iconFromHandle.Clone();
        }
        finally
        {
            // Always destroy the original HICON to prevent GDI handle leaks
            DestroyIcon(handle);
        }
    }

    private void ShowNotification(string message, ToolTipIcon icon)
    {
        try
        {
            if (!_isDisposed && _icon != null)
            {
                _icon.ShowBalloonTip(2000, Resources.ScannerNotificationTitle, message, icon);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, NotificationFailedFormat, ex.Message));
        }
    }

    private void OnExit(object? sender, EventArgs e)
    {
        var result = MessageBox.Show(
            Resources.ExitConfirmationText,
            Resources.ExitConfirmationTitle,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result == DialogResult.Yes)
        {
            // Dispose performs the (bounded) stop; calling StopService here as well would race it.
            Dispose();
            System.Windows.Forms.Application.Exit();
        }
    }

    private void OnRestartAsAdmin(object? sender, EventArgs e)
    {
        var result = MessageBox.Show(
            "This will restart the application with administrator privileges.\n\n" +
            "Admin privileges may be required for:\n" +
            "• Better scanner hardware access\n" +
            "• Access to some scanner drivers (TWAIN)\n\n" +
            "Continue?",
            "Restart as Administrator",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        // Elevate FIRST and dispose only on success: a declined UAC prompt (Win32Exception
        // 1223) is a routine path, and disposing before knowing the outcome left a disposed,
        // invisible process with no tray icon and no way back. The new instance tolerates the
        // brief port overlap via its own port-fallback search.
        if (result == DialogResult.Yes && Program.RestartAsAdministrator())
        {
            // Dispose performs the (bounded) stop; calling StopService here as well would race it.
            Dispose();

            // Successfully restarted as admin, exit current instance
            System.Windows.Forms.Application.Exit();
        }
    }
}
