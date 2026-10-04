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

/// <summary>
/// DTO for partial profile updates. Only provided fields are updated.
/// Null values mean "don't change this field" - they are NOT used to clear fields.
/// POST creates a new profile from UpsertProfileDto; PATCH updates the provided fields only.
/// There is no full-replace endpoint, and DeviceId cannot be cleared once set.
/// </summary>
public record UpdateProfileDto(
    string? Name = null,
    string? DeviceId = null,
    string? PaperSource = null,
    string? BitDepth = null,
    int? Resolution = null,
    string? PageSize = null,
    string? HorizontalAlign = null,
    string? Scale = null,
    int? Brightness = null,
    int? Contrast = null,
    int? ImageQuality = null
);
