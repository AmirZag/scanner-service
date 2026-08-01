namespace ScannerService.Domain.Entities;

public class ExportSetting
{
    public int Id { get; set; }
    public string Format { get; set; } = Common.ApplicationConstants.ExportDefaults.DefaultFormat;
    public string ExportPath { get; set; } = "";
    public string FileName { get; set; } = Common.ApplicationConstants.ExportDefaults.DefaultFileName;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Updates the export setting with the provided values. Only non-null parameters update the entity.
    /// </summary>
    public void Update(string? format = null, string? exportPath = null, string? fileName = null)
    {
        if (format != null)
        {
            Format = format;
        }
        if (exportPath != null)
        {
            ExportPath = exportPath;
        }
        if (fileName != null)
        {
            FileName = fileName;
        }

        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Creates a default ExportSetting with standard values.
    /// </summary>
    public static ExportSetting CreateDefault()
    {
        return new ExportSetting
        {
            Format = Common.ApplicationConstants.ExportDefaults.DefaultFormat,
            ExportPath = "",
            FileName = Common.ApplicationConstants.ExportDefaults.DefaultFileName
        };
    }
}
