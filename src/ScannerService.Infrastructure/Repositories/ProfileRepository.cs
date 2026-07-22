using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScannerService.Application.DTOs;
using ScannerService.Application.Interfaces;
using ScannerService.Domain.Entities;
using ScannerService.Infrastructure.Persistence;

namespace ScannerService.Infrastructure.Repositories;

public class ProfileRepository : IProfileRepository
{

    private readonly Context _context;
    private readonly ILogger<ProfileRepository> _logger;

    public ProfileRepository(Context context, ILogger<ProfileRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<ProfileDto>> GetAllAsync()
    {
        _logger.LogDebug("Retrieving all profiles");
        return await _context.Profiles.Select(MapToDto).ToListAsync();
    }

    public async Task<ProfileDto?> GetByIdAsync(int id)
    {
        _logger.LogDebug("Retrieving profile {ProfileId}", id);
        return await _context.Profiles.Where(x => x.Id == id).Select(MapToDto).FirstOrDefaultAsync();
    }

    public async Task<ProfileDto> AddAsync(UpsertProfileDto upsertProfileDto)
    {
        _logger.LogInformation("Adding profile {ProfileName}", upsertProfileDto.Name);

        var entity = new Profile
        {
            Name = upsertProfileDto.Name,
            DeviceId = upsertProfileDto.DeviceId,
            PaperSource = upsertProfileDto.PaperSource,
            BitDepth = upsertProfileDto.BitDepth,
            PageSize = upsertProfileDto.PageSize,
            HorizontalAlign = upsertProfileDto.HorizontalAlign,
            Resolution = upsertProfileDto.Resolution,
            Scale = upsertProfileDto.Scale,
            Brightness = upsertProfileDto.Brightness,
            Contrast = upsertProfileDto.Contrast,
            ImageQuality = upsertProfileDto.ImageQuality,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Profiles.Add(entity);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Profile added with ID {ProfileId}", entity.Id);
        return ToDto(entity);
    }

    public async Task<ProfileDto?> UpdateAsync(int id, UpdateProfileDto updateProfileDto)
    {
        _logger.LogInformation("Updating Profile {ProfileId} with partial update", id);

        var entity = await _context.Profiles.FindAsync(id);
        if (entity is null)
        {
            _logger.LogWarning("Profile {ProfileId} not found for update", id);
            return null;
        }

        var hasChanges = false;

        if (updateProfileDto.Name != null)
        {
            entity.Name = updateProfileDto.Name;
            hasChanges = true;
        }

        if (updateProfileDto.DeviceId != null)
        {
            entity.DeviceId = updateProfileDto.DeviceId;
            hasChanges = true;
        }

        if (updateProfileDto.PaperSource != null)
        {
            entity.PaperSource = updateProfileDto.PaperSource;
            hasChanges = true;
        }

        if (updateProfileDto.BitDepth != null)
        {
            entity.BitDepth = updateProfileDto.BitDepth;
            hasChanges = true;
        }

        if (updateProfileDto.PageSize != null)
        {
            entity.PageSize = updateProfileDto.PageSize;
            hasChanges = true;
        }

        if (updateProfileDto.HorizontalAlign != null)
        {
            entity.HorizontalAlign = updateProfileDto.HorizontalAlign;
            hasChanges = true;
        }

        if (updateProfileDto.Resolution != null)
        {
            entity.Resolution = updateProfileDto.Resolution.Value;
            hasChanges = true;
        }

        if (updateProfileDto.Scale != null)
        {
            entity.Scale = updateProfileDto.Scale;
            hasChanges = true;
        }

        if (updateProfileDto.Brightness != null)
        {
            entity.Brightness = updateProfileDto.Brightness.Value;
            hasChanges = true;
        }

        if (updateProfileDto.Contrast != null)
        {
            entity.Contrast = updateProfileDto.Contrast.Value;
            hasChanges = true;
        }

        if (updateProfileDto.ImageQuality != null)
        {
            entity.ImageQuality = updateProfileDto.ImageQuality.Value;
            hasChanges = true;
        }

        if (hasChanges)
        {
            entity.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            _logger.LogInformation("Profile {ProfileId} updated successfully", id);
        }
        else
        {
            _logger.LogInformation("Profile {ProfileId} update requested but no changes to apply", id);
        }

        return ToDto(entity);
    }
    public async Task<bool> DeleteAsync(int id)
    {
        _logger.LogInformation("Deleting profile {ProfileId}", id);

        var entity = await _context.Profiles.FindAsync(id);
        if (entity is null)
        {
            _logger.LogWarning("Profile {ProfileId} not found for deletion", id);
            return false;
        }

        _context.Profiles.Remove(entity);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Profile {ProfileId} deleted successfully", id);
        return true;
    }



    private static ProfileDto ToDto(Profile p) => new(
        p.Id,
        p.Name,
        p.DeviceId,
        p.PaperSource,
        p.BitDepth,
        p.PageSize,
        p.HorizontalAlign,
        p.Resolution,
        p.Scale,
        p.Brightness,
        p.Contrast,
        p.ImageQuality,
        p.CreatedAt,
        p.UpdatedAt
    );

    private static readonly Expression<Func<Profile, ProfileDto>> MapToDto = p => new ProfileDto
    (
        p.Id,
        p.Name,
        p.DeviceId,
        p.PaperSource,
        p.BitDepth,
        p.PageSize,
        p.HorizontalAlign,
        p.Resolution,
        p.Scale,
        p.Brightness,
        p.Contrast,
        p.ImageQuality,
        p.CreatedAt,
        p.UpdatedAt
    );
}
