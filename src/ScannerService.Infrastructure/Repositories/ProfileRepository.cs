using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScannerService.Application.Common;
using ScannerService.Application.DTOs;
using ScannerService.Application.Interfaces;
using ScannerService.Domain.Common;
using ScannerService.Domain.Entities;
using ScannerService.Infrastructure.Persistence;

namespace ScannerService.Infrastructure.Repositories;

public class ProfileRepository : RepositoryBase<Profile>, IProfileRepository
{
    public ProfileRepository(Context context, ILogger<ProfileRepository> logger)
        : base(context, logger)
    {
    }

    public async Task<List<ProfileDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        Logger.LogDebug("Retrieving all profiles");
        return await Context.Profiles.Select(p => MapToProfileDto(p)).ToListAsync(cancellationToken);
    }

    public new async Task<ProfileDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        Logger.LogDebug("Retrieving profile {ProfileId}", id);
        var entity = await Context.Profiles.FindAsync([id], cancellationToken);
        return entity == null ? null : MapToProfileDto(entity);
    }

    public async Task<ProfileDto> AddAsync(UpsertProfileDto upsertProfileDto, CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("Adding profile {ProfileName}", upsertProfileDto.Name);

        var entity = Profile.Create(
            upsertProfileDto.Name,
            upsertProfileDto.DeviceId,
            upsertProfileDto.PaperSource,
            upsertProfileDto.BitDepth,
            upsertProfileDto.PageSize,
            upsertProfileDto.HorizontalAlign,
            upsertProfileDto.Resolution,
            upsertProfileDto.Scale,
            upsertProfileDto.Brightness,
            upsertProfileDto.Contrast,
            upsertProfileDto.ImageQuality
        );

        Context.Profiles.Add(entity);
        await Context.SaveChangesAsync(cancellationToken);

        Logger.LogInformation("Profile added with ID {ProfileId}", entity.Id);
        return MapToProfileDto(entity);
    }

    public async Task<ProfileDto?> UpdateAsync(int id, UpdateProfileDto updateProfileDto, CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("Updating Profile {ProfileId} with partial update", id);

        var entity = await Context.Profiles.FindAsync([id], cancellationToken);
        if (entity is null)
        {
            Logger.LogWarning("Profile {ProfileId} not found for update", id);
            return null;
        }

        var oldUpdatedAt = entity.UpdatedAt;
        var updateOptions = updateProfileDto.ToUpdateOptions();
        entity.Update(updateOptions);

        if (entity.UpdatedAt != oldUpdatedAt)
        {
            await Context.SaveChangesAsync(cancellationToken);
            Logger.LogInformation("Profile {ProfileId} updated successfully", id);
        }
        else
        {
            Logger.LogInformation("Profile {ProfileId} update requested but no changes to apply", id);
        }

        return MapToProfileDto(entity);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("Deleting profile {ProfileId}", id);

        var entity = await Context.Profiles.FindAsync([id], cancellationToken);
        if (entity is null)
        {
            Logger.LogWarning("Profile {ProfileId} not found for deletion", id);
            return false;
        }

        Context.Profiles.Remove(entity);
        await Context.SaveChangesAsync(cancellationToken);

        Logger.LogInformation("Profile {ProfileId} deleted successfully", id);
        return true;
    }

    /// <summary>
    /// Maps a Profile entity to ProfileDto.
    /// Consolidated mapping method used throughout the repository.
    /// </summary>
    private static ProfileDto MapToProfileDto(Profile p) => new(
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
