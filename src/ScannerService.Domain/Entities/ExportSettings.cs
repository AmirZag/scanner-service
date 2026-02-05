namespace ScannerService.Domain.Entities;

public class ExportSettings
{
    public int Id { get; set; }
    public string OutputFormat { get; set; } = "PDF";
    public string OutputDirectory { get; set; } = "";
    public string FileName { get; set; } = "scan_{datetime}";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}