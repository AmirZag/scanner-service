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
    public async Task<ProfileDto?> UpdateAsync(int id, UpsertProfileDto upsertProfileDto)
    {
        _logger.LogInformation("Updating Profile {ProfileId}", id);

        var entity = await _context.Profiles.FindAsync(id);
        if (entity is null)
        {
            _logger.LogWarning("Profile {ProfileId} not found for update", id);
            return null;
        }

        entity.Name = upsertProfileDto.Name;
        entity.DeviceId = upsertProfileDto.DeviceId;
        entity.PaperSource = upsertProfileDto.PaperSource;
        entity.BitDepth = upsertProfileDto.BitDepth;
        entity.PageSize = upsertProfileDto.PageSize;
        entity.HorizontalAlign = upsertProfileDto.HorizontalAlign;
        entity.Resolution = upsertProfileDto.Resolution;
        entity.Scale = upsertProfileDto.Scale;
        entity.Brightness = upsertProfileDto.Brightness;
        entity.Contrast = upsertProfileDto.Contrast;
        entity.ImageQuality = upsertProfileDto.ImageQuality;
        entity.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Profile {ProfileId} updated successfully", id);

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
