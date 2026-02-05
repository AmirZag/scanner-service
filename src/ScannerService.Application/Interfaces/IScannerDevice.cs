using ScannerService.Application.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ScannerService.Application.Interfaces;

public interface IScannerDevice
{
    Task<List<ScannerDto>> GetScannersAsync();
}