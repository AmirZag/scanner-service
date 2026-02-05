using ScannerService.Application.DTOs;
using System.Threading.Tasks;

namespace ScannerService.Application.Interfaces;

public interface IScanService
{
    Task<ScanResult> ScanAsync(ScanRequest request);
}