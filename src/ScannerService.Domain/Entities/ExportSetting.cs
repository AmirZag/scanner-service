namespace ScannerService.Domain.Entities;

internal class ExportSetting
{
    public int Id { get; set; }
    public string Format { get; set; } = "PDF";
    public string ExportPath { get; set; } = "";
    public string FileName { get; set; } = "scan_{datetime}";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
