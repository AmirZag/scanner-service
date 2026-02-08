using ScannerService.Application.DTOs;

namespace ScannerService.Application.Interfaces;

public interface IScanJobService
{
    Task<ScanResultDto> StartScanJobAsync(ScanRequestDto req);
}
