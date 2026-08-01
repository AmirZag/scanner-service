namespace ScannerService.Domain.Common;

/// <summary>
/// Options pattern for updating a Profile entity.
/// Allows partial updates where only specified properties are modified.
/// </summary>
public class ProfileUpdateOptions
{
    public string? Name { get; set; }
    public string? DeviceId { get; set; }
    public string? PaperSource { get; set; }
    public string? BitDepth { get; set; }
    public string? PageSize { get; set; }
    public string? HorizontalAlign { get; set; }
    public int? Resolution { get; set; }
    public string? Scale { get; set; }
    public int? Brightness { get; set; }
    public int? Contrast { get; set; }
    public int? ImageQuality { get; set; }
}
