using ScannerService.Application.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ScannerService.Application.Interfaces;

public interface IProfileRepository
{
    Task<List<ProfileDto>> GetAllAsync();
    Task<ProfileDto?> GetByIdAsync(int id);
    Task<ProfileDto> AddAsync(UpsertProfile dto);
    Task<ProfileDto?> UpdateAsync(int id, UpsertProfile dto);
    Task<bool> DeleteAsync(int id);
}