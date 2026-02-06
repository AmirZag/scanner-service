using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ScannerService.Application.DTOs;

namespace ScannerService.Application.Interfaces;

internal interface IProfileRepository
{
    Task<List<ProfileDto>> GetAllAsync();
    Task<ProfileDto?> GetByIdAsync();
    Task<ProfileDto> AddAsync();
    Task<ProfileDto?> UpdateAsync(int id, UpsertProfileDto upsertProfileDto);
    Task<bool> DeleteAsync(int id);
}
