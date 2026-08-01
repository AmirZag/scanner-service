using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ScannerService.Application.DTOs;

namespace ScannerService.Application.Interfaces;

public interface IProfileRepository
{
    Task<List<ProfileDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<ProfileDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<ProfileDto> AddAsync(UpsertProfileDto upsertProfileDto, CancellationToken cancellationToken = default);
    Task<ProfileDto?> UpdateAsync(int id, UpdateProfileDto updateProfileDto, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
