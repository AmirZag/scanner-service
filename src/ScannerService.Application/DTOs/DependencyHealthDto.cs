namespace ScannerService.Application.DTOs;

/// <summary>
/// Represents the health status of a system dependency.
/// </summary>
public record DependencyHealthDto(string Name, bool IsHealthy);

/// <summary>
/// Extended health check response with dependency status.
/// </summary>
public record DetailedApiHealthCheckDto(
    bool IsHealthy,
    string Version,
    Dictionary<string, bool> Dependencies,
    string? CorrelationId = null
)
{
    /// <summary>
    /// Creates a successful health check with all dependencies healthy.
    /// </summary>
    public static DetailedApiHealthCheckDto Healthy(string version, Dictionary<string, bool> dependencies, string? correlationId = null)
        => new(true, version, dependencies, correlationId);

    /// <summary>
    /// Creates a failed health check with dependency details.
    /// </summary>
    public static DetailedApiHealthCheckDto Unhealthy(string version, Dictionary<string, bool> dependencies, string? correlationId = null)
        => new(false, version, dependencies, correlationId);
}
