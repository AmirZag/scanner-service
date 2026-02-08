namespace ScannerService.Application.DTOs;

public class ScanJobConfiguration
{
    public string DeviceId { get; set; } = string.Empty;
    public string PaperSource { get; set; } = "Glass";
    public string BitDepth { get; set; } = "Color";
    public int Resolution { get; set; } = 200;
    public int Brightness { get; set; }
    public int Contrast { get; set; }
    public int ImageQuality { get; set; } = 85;
    public string Format { get; set; } = "PDF";
    public string ExportPath { get; set; } = "";
    public string FileName { get; set; } = "scan_{datetime}";
}
