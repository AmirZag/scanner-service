namespace ScannerService.Application.DTOs;

internal record ApiHealthCheckDto(
     bool IsRunning,
     string Version
);
