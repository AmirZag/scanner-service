using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using NAPS2.Images;
using NAPS2.Pdf;
using NAPS2.Scan;
using ScannerService.Application.Interfaces;

namespace ScannerService.Infrastructure.Services;

/// <summary>
/// Internal contract for scanner initialization context access.
/// </summary>
internal interface IScannerInitializerContext
{
    ScanningContext Context { get; }
    ScanController Controller { get; }
}

/// <summary>
/// Implementation of the scanner initializer service.
/// Handles lazy initialization of the scanner subsystem.
/// Thread-safe with proper error handling.
/// </summary>
public sealed class ScannerInitializer : IScannerInitializer, IScannerInitializerContext, IAsyncDisposable
{
    private ScanningContext? _context;
    private ScanController? _controller;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ILogger<ScannerInitializer> _logger;
    private Task? _initializationTask;
    private volatile bool _isInitialized;

    public bool IsInitialized => _isInitialized;
    public bool TwainWorkerFailed { get; private set; }
    ScanningContext IScannerInitializerContext.Context => _context ?? throw new InvalidOperationException("Scanner context not initialized");
    ScanController IScannerInitializerContext.Controller => _controller ?? throw new InvalidOperationException("Scan controller not initialized");

    public ScannerInitializer(ILogger<ScannerInitializer> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
    }

    private Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        // Fast path for already initialized case (no lock needed for reading)
        if (_isInitialized)
        {
            return Task.CompletedTask;
        }

        // Slow path: need to initialize - use lock for thread safety
        return EnsureInitializedSlowPathAsync(cancellationToken);
    }

    private async Task EnsureInitializedSlowPathAsync(CancellationToken cancellationToken)
    {
        // If a task is already in progress, just wait for it
        if (_initializationTask != null)
        {
            await _initializationTask.ConfigureAwait(false);
            return;
        }

        // Acquire lock to create new initialization task
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // Double-check after acquiring lock
            if (_isInitialized)
            {
                return;
            }

            // Create the initialization task
            _initializationTask = Task.Run(() => InitializeInternal(), cancellationToken);
            await _initializationTask.ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private void InitializeInternal()
    {
        // Note: This method is called while holding the lock
        // No additional locking needed here

        if (_isInitialized)
        {
            return;
        }

        _logger.LogInformation("Initializing scanner context");

        try
        {
            ImageContext imageContext = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? new NAPS2.Images.Gdi.GdiImageContext()
                : new NAPS2.Images.ImageSharp.ImageSharpImageContext();

            _context = new ScanningContext(imageContext);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    _context.SetUpWin32Worker();
                    _logger.LogInformation("TWAIN Worker initialized successfully");
                }
                catch (Exception ex)
                {
                    TwainWorkerFailed = true;
                    _logger.LogWarning(ex, "TWAIN worker setup failed. TWAIN scanning will be unavailable, but WIA and ESCL will work normally");
                }
            }

            _controller = new ScanController(_context);
            _isInitialized = true;
            _logger.LogInformation("Scanner initialization complete");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scanner initialization failed");
            _isInitialized = false;
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _logger.LogInformation("Disposing scanner initializer");

        try
        {
            // Worker teardown can block or race a cold-start initialization during shutdown; a failure
            // here must not break the shutdown sequence.
            _context?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispose scanner context cleanly");
        }

        _lock.Dispose();

        await Task.CompletedTask;
    }
}
