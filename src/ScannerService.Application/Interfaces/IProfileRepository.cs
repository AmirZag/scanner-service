using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ScannerService.Application.DTOs;

namespace ScannerService.Application.Interfaces;

public interface IProfileRepository
{
    Task<List<ProfileDto>> GetAllAsync();
    Task<ProfileDto?> GetByIdAsync(int id);
    Task<ProfileDto> AddAsync(UpsertProfileDto upsertProfileDto);
    Task<ProfileDto?> UpdateAsync(int id, UpsertProfileDto upsertProfileDto);
    Task<bool> DeleteAsync(int id);
}
