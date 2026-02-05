namespace ScannerService.Application.DTOs;

public record ScannerDto(
    string Id,
    string Name,
    string Driver
);

public record HealthDto(
    bool IsRunning,
    string Version
);