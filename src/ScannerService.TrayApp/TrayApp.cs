using Microsoft.Extensions.Configuration;
using ScannerService.TrayApp.Configurations;
using ScannerService.TrayApp.Services;
using Serilog;
using Serilog.Events;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Http;
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

    private bool _isDisposed;
    private bool _isActuallyRunning;
    private readonly object _stateLock = new object();

    private readonly ScannerServiceConfiguration _config;
    private readonly string _apiHealthUrl;

    public TrayApp()
    {
        // Initialize early logging
        InitializeEarlyLogging();

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        _config = configuration.GetSection("ScannerService")
            .Get<ScannerServiceConfiguration>() ?? new ScannerServiceConfiguration();

        _apiHealthUrl = $"http://localhost:{_config.ApiPort}/api/health";

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
            Text = "Scanner Service - Stopped",
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
            return;

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
            Text = "● اسکنر متوقف شده",
            Enabled = false,
            Font = new Font(menu.Font, FontStyle.Bold),
            ForeColor = Color.Red
        };
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());

        _startMenuItem = new ToolStripMenuItem("شروع", null, (s, e) => StartService())
        {
            Name = "start"
        };
        _stopMenuItem = new ToolStripMenuItem("توقف", null, (s, e) => StopService())
        {
            Name = "stop"
        };

        menu.Items.Add(_startMenuItem);
        menu.Items.Add(_stopMenuItem);
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(new ToolStripMenuItem("رابط نرم افزاری", null, (s, e) => OpenApiDocs()));
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(new ToolStripMenuItem("خروج", null, OnExit));

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
            ShowNotification("Please start the service first", ToolTipIcon.Warning);
            return;
        }

        try
        {
            var url = $"http://localhost:{_config.ApiPort}/scalar/v1";
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
                $"Failed to open API documentation:\n{ex.Message}",
                "Error",
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
            _startMenuItem.Enabled = !running;

        if (_stopMenuItem != null)
            _stopMenuItem.Enabled = running;
    }

    private void OnStatusCheckTimerTick(object? sender, EventArgs e)
    {
        _ = CheckStatusAsync();
    }

    private async Task CheckStatusAsync()
    {
        if (_isDisposed) return;

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
                    UpdateUI(isResponding);
            }, null);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Status check failed: {ex.Message}");
        }
    }

    private void UpdateUI(bool running)
    {
        try
        {
            if (_statusItem != null)
            {
                _statusItem.Text = running
                    ? $"● اسکنر فعال است - localhost:{_config.ApiPort}"
                    : "● اسکنر غیرفعال است";
                _statusItem.ForeColor = running ? Color.Green : Color.Red;
            }

            if (!_isDisposed && _icon != null)
            {
                var oldIcon = _icon.Icon;
                _icon.Icon = CreateIcon(running);
                _icon.Text = running ? "اسکنر فعال" : "اسکنر غیرفعال";
                oldIcon?.Dispose();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"UI update failed: {ex.Message}");
        }
    }

    private void StartService()
    {
        if (_isDisposed) return;

        Task.Run(async () =>
        {
            try
            {
                if (_webApiHost.IsRunning)
                {
                    _syncContext.Post(_ =>
                    {
                        ShowNotification("Already running", ToolTipIcon.Info);
                    }, null);
                    return;
                }

                await _webApiHost.StartAsync();

                _syncContext.Post(_ =>
                {
                    ShowNotification("Started", ToolTipIcon.Info);
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
                        $"Failed to start service:\n{ex.Message}",
                        "Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);

                    UpdateUI(false);
                }, null);
            }
        });
    }

    private void StopService()
    {
        if (_isDisposed) return;

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
                    ShowNotification("Stopped", ToolTipIcon.Info);
                }, null);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to stop service");
                _syncContext.Post(_ =>
                {
                    MessageBox.Show(
                        $"Failed to stop service:\n{ex.Message}",
                        "Error",
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
            Debug.WriteLine($"Health check failed: {ex.Message}");
            return false;
        }
    }

    private Icon CreateIcon(bool running)
    {
        var bmp = new Bitmap(16, 16);

        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            using (var pen = new Pen(Color.Black, 1))
            using (var statusBrush = new SolidBrush(running ? Color.LimeGreen : Color.Red))
            {
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
        }

        return Icon.FromHandle(bmp.GetHicon());
    }

    private void ShowNotification(string message, ToolTipIcon icon)
    {
        try
        {
            if (!_isDisposed && _icon != null)
            {
                _icon.ShowBalloonTip(2000, "Scanner", message, icon);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to show notification: {ex.Message}");
        }
    }

    private void OnExit(object? sender, EventArgs e)
    {
        var result = MessageBox.Show(
            "میخواهید اسکنر را ببندید؟\n\nسرویس اسکنر در دسترس نخواهد بود",
            "بستن",
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