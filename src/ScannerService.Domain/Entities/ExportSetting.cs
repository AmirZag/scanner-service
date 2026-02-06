using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScannerService.Domain.Entities;

internal class ExportSetting
{
    public int Id { get; set; }
    public string OutputFormat { get; set; } = "PDF";
    public string OutputDirectory { get; set; } = "";
    public string FileName { get; set; } = "scan_{datetime}";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
