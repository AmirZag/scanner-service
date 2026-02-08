namespace ScannerService.Application.DTOs;

public record ApiHealthCheckDto(
     bool IsRunning,
     string Version
);
