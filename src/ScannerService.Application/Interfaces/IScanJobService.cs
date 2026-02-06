using ScannerService.Application.DTOs;

namespace ScannerService.Application.Interfaces;

internal interface IScanJobService
{
    Task<ScanResultDto> StartScanJobAsync();
}
