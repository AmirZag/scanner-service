using ScannerService.Application.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ScannerService.Application.Interfaces;

public interface IScannerDeviceService
{
    Task<List<string>> DeviceScanAsync(ScanJobConfiguration config);
}