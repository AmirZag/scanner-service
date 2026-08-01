using ScannerService.Application.Common;
using ScannerService.Application.DTOs;

namespace ScannerService.Application.Interfaces;

/// <summary>
/// Scanner service interface for executing scan operations.
/// All methods return Result types for consistent error handling.
/// </summary>
public interface IScannerService
{
    /// <summary>
    /// Executes a scan operation with the specified configuration.
    /// Returns Result{List{string}} containing the paths to the output files.
    /// </summary>
    Task<Result<List<string>>> ExecuteScanAsync(ScanJobConfiguration scanJobConfiguration, CancellationToken cancellationToken = default);
}
