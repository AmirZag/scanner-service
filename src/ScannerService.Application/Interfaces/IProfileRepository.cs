using ScannerService.Application.Common;
using ScannerService.Application.DTOs;

namespace ScannerService.Application.Interfaces;

/// <summary>
/// Interface for profile repository operations.
/// Operations that can fail return Result types for consistent error handling.
/// </summary>
public interface IProfileRepository
{
    Task<List<ProfileDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<ProfileDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<ProfileDto> AddAsync(UpsertProfileDto upsertProfileDto, CancellationToken cancellationToken = default);
    Task<Result<ProfileDto>> UpdateAsync(int id, UpdateProfileDto updateProfileDto, CancellationToken cancellationToken = default);
    Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
