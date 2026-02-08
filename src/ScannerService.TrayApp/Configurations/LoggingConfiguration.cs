namespace ScannerService.TrayApp.Configurations;

public class LoggingConfiguration
{
    public FileLoggingConfiguration File { get; set; } = new();
}

public class FileLoggingConfiguration
{
    public string Path { get; set; } = "logs/scanner-.log";
    public string RollingInterval { get; set; } = "Day";
    public int RetainedFileCountLimit { get; set; } = 7;
    public long FileSizeLimitBytes { get; set; } = 10_485_760; // 10 MB
    public bool RollOnFileSizeLimit { get; set; } = true;
}
