using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ScannerService.Application.DTOs;

namespace ScannerService.Application.Interfaces;

public interface IScannerQueries
{
    Task<List<ScannerDto>> GetScannersListAsync();
}
