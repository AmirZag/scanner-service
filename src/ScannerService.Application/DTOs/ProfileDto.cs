namespace ScannerService.Application.DTOs;

public record ProfileDto(
    int Id,
    string Name,
    string? DeviceId,
    string PaperSource,
    string BitDepth,
    string PageSize,
    string HorizontalAlign,
    int Resolution,
    string Scale,
    int Brightness,
    int Contrast,
    int ImageQuality,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public record UpsertProfileDto(
    string Name,
    string? DeviceId = null,
    string PaperSource = "Glass",
    string BitDepth = "Color",
    string PageSize = "A4",
    string HorizontalAlign = "Center",
    int Resolution = 200,
    string Scale = "1:1",
    int Brightness = 0,
    int Contrast = 0,
    int ImageQuality = 85
);
