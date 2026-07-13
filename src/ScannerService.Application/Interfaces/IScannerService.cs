using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ScannerService.Application.DTOs;

namespace ScannerService.Application.Interfaces;

public interface IScannerService
{
    Task<ScanExecutionResult> ExecuteScanAsync(ScanJobConfiguration scanJobConfiguration);
}
