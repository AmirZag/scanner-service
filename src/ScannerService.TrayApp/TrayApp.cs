using Microsoft.Extensions.Configuration;
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
    private readonly NotifyIcon _icon;
    private readonly System.Windows.Forms.Timer _statusCheckTimer;
    private readonly HttpClient _httpClient;
    private readonly SynchronizationContext _syncContext;
    private readonly WebApiHostService _webApiHost;

    private ToolStripMenuItem? _statusItem;
    private ToolStripMenuItem? _startMenuItem;
    private ToolStripMenuItem? _stopMenuItem;
    private ToolStripMenuItem? _apiDocsMenuItem;

    private bool _isDisposed;
    private bool _isActuallyRunning;
    private readonly object _stateLock = new object();

    private readonly ScannerServiceConfiguration _config;
    private readonly string _apiHealthUrl;

    private static readonly CompositeFormat StatusRunningFormat = CompositeFormat.Parse(Resources.StatusRunningPersianFormat);
    private static readonly CompositeFormat ApiUrlFormat = CompositeFormat.Parse("http://localhost:{0}/api/health");
    private static readonly CompositeFormat ScalarUrlFormat = CompositeFormat.Parse("http://localhost:{0}/scalar/openapi");
    private static readonly CompositeFormat FailedToOpenApiDocsFormat = CompositeFormat.Parse(Resources.FailedToOpenApiDocsFormat);
    private static readonly CompositeFormat FailedToStartServiceFormat = CompositeFormat.Parse(Resources.FailedToStartServiceFormat);
    private static readonly CompositeFormat FailedToStopServiceFormat = CompositeFormat.Parse(Resources.FailedToStopServiceFormat);
    private static readonly CompositeFormat StatusCheckFailedFormat = CompositeFormat.Parse(Resources.StatusCheckFailedFormat);
    private static readonly CompositeFormat UiUpdateFailedFormat = CompositeFormat.Parse(Resources.UiUpdateFailedFormat);
    private static readonly CompositeFormat HealthCheckFailedFormat = CompositeFormat.Parse(Resources.HealthCheckFailedFormat);
    private static readonly CompositeFormat NotificationFailedFormat = CompositeFormat.Parse(Resources.NotificationFailedFormat);

    public TrayApp()
    {
        InitializeEarlyLogging();

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        _config = configuration.GetSection("ScannerService")
            .Get<ScannerServiceConfiguration>() ?? new ScannerServiceConfiguration();

        _apiHealthUrl = string.Format(CultureInfo.InvariantCulture, ApiUrlFormat, _config.ApiPort);

        _syncContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMilliseconds(_config.HttpTimeout)
        };

        _webApiHost = new WebApiHostService(_config.ApiPort);

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

    private void InitializeEarlyLogging()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();

        var loggingConfig = configuration.GetSection("Logging")
            .Get<LoggingConfiguration>() ?? new LoggingConfiguration();

        var logPath = Path.Combine(AppContext.BaseDirectory, loggingConfig.File.Path);
        var logDirectory = Path.GetDirectoryName(logPath);

        if (!string.IsNullOrEmpty(logDirectory) && !Directory.Exists(logDirectory))
        {
            Directory.CreateDirectory(logDirectory);
        }

        var rollingInterval = loggingConfig.File.RollingInterval.ToLowerInvariant() switch
        {
            "minute" => RollingInterval.Minute,
            "hour" => RollingInterval.Hour,
            "day" => RollingInterval.Day,
            "month" => RollingInterval.Month,
            "year" => RollingInterval.Year,
            _ => RollingInterval.Day
        };

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                logPath,
                formatProvider: CultureInfo.InvariantCulture,
                rollingInterval: rollingInterval,
                retainedFileCountLimit: loggingConfig.File.RetainedFileCountLimit,
                fileSizeLimitBytes: loggingConfig.File.FileSizeLimitBytes,
                rollOnFileSizeLimit: loggingConfig.File.RollOnFileSizeLimit,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("Scanner Service Tray Application starting");
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

            _webApiHost?.Dispose();
            _httpClient?.Dispose();

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
            var url = string.Format(CultureInfo.InvariantCulture, ScalarUrlFormat, _config.ApiPort);
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

        if (_startMenuItem != null)
        {
            _startMenuItem.Enabled = !running;
        }

        if (_stopMenuItem != null)
        {
            _stopMenuItem.Enabled = running;
        }

        if (_apiDocsMenuItem != null)
        {
            _apiDocsMenuItem.Enabled = running;
        }
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
                    ? string.Format(CultureInfo.CurrentCulture, StatusRunningFormat, _config.ApiPort)
                    : Resources.StatusInactivePersian;
                _statusItem.ForeColor = running ? Color.Green : Color.Red;
            }

            if (_startMenuItem != null)
            {
                _startMenuItem.Enabled = !running;
            }

            if (_stopMenuItem != null)
            {
                _stopMenuItem.Enabled = running;
            }

            if (_apiDocsMenuItem != null)
            {
                _apiDocsMenuItem.Enabled = running;
            }

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

        Task.Run(async () =>
        {
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

                _syncContext.Post(_ =>
                {
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

        Task.Run(async () =>
        {
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

    private async Task<bool> IsApiRespondingAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync(_apiHealthUrl);
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

        return Icon.FromHandle(bmp.GetHicon());
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
            StopService();
            Dispose();
            System.Windows.Forms.Application.Exit();
        }
    }
}
