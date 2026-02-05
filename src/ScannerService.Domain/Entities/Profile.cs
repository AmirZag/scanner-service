namespace ScannerService.Domain.Entities;

public class Profile
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? DeviceId { get; set; }
    public string PaperSource { get; set; } = "Glass";
    public string BitDepth { get; set; } = "Color";
    public string PageSize { get; set; } = "A4";
    public string HorizontalAlign { get; set; } = "Center";
    public int Resolution { get; set; } = 200;
    public string Scale { get; set; } = "1:1";
    public int Brightness { get; set; } = 0;
    public int Contrast { get; set; } = 0;
    public int ImageQuality { get; set; } = 85;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}