using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ScannerService.Application.DTOs;

using System.Threading;

namespace ScannerService.Application.Interfaces;

public interface IScannerQueries
{
    Task<List<ScannerDto>> GetScannersListAsync(CancellationToken cancellationToken = default);
}
