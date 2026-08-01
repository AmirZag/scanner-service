namespace ScannerService.Application.Interfaces;

/// <summary>
/// Service responsible for initializing the scanner subsystem.
/// Separates initialization concerns from scanning operations.
/// </summary>
public interface IScannerInitializer
{
    /// <summary>
    /// Initializes the scanner context and controller.
    /// Must be called before any scanning operations.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets whether the scanner subsystem has been initialized.
    /// </summary>
    bool IsInitialized { get; }

    /// <summary>
    /// Gets whether TWAIN worker failed during initialization.
    /// When true, TWAIN scanning is unavailable but WIA/ESCL may work.
    /// </summary>
    bool TwainWorkerFailed { get; }
}
