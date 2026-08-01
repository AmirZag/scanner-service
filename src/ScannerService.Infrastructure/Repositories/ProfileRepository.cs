using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScannerService.Application.DTOs;
using ScannerService.Application.Interfaces;
using ScannerService.Domain.Entities;
using ScannerService.Infrastructure.Persistence;

namespace ScannerService.Infrastructure.Repositories;

public class ProfileRepository : RepositoryBase<Profile>, IProfileRepository
{
    public ProfileRepository(Context context, ILogger<ProfileRepository> logger)
        : base(context, logger)
    {
    }

    public async Task<List<ProfileDto>> GetAllAsync()
    {
        Logger.LogDebug("Retrieving all profiles");
        return await Context.Profiles.Select(MapToDto).ToListAsync();
    }

    public new async Task<ProfileDto?> GetByIdAsync(int id)
    {
        Logger.LogDebug("Retrieving profile {ProfileId}", id);
        return await Context.Profiles.Where(x => x.Id == id).Select(MapToDto).FirstOrDefaultAsync();
    }

    public async Task<ProfileDto> AddAsync(UpsertProfileDto upsertProfileDto)
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
        await Context.SaveChangesAsync();

        Logger.LogInformation("Profile added with ID {ProfileId}", entity.Id);
        return ToDto(entity);
    }

    public async Task<ProfileDto?> UpdateAsync(int id, UpdateProfileDto updateProfileDto)
    {
        Logger.LogInformation("Updating Profile {ProfileId} with partial update", id);

        var entity = await Context.Profiles.FindAsync(id);
        if (entity is null)
        {
            Logger.LogWarning("Profile {ProfileId} not found for update", id);
            return null;
        }

        var oldUpdatedAt = entity.UpdatedAt;
        entity.Update(
            updateProfileDto.Name,
            updateProfileDto.DeviceId,
            updateProfileDto.PaperSource,
            updateProfileDto.BitDepth,
            updateProfileDto.PageSize,
            updateProfileDto.HorizontalAlign,
            updateProfileDto.Resolution,
            updateProfileDto.Scale,
            updateProfileDto.Brightness,
            updateProfileDto.Contrast,
            updateProfileDto.ImageQuality
        );

        if (entity.UpdatedAt != oldUpdatedAt)
        {
            await Context.SaveChangesAsync();
            Logger.LogInformation("Profile {ProfileId} updated successfully", id);
        }
        else
        {
            Logger.LogInformation("Profile {ProfileId} update requested but no changes to apply", id);
        }

        return ToDto(entity);
    }
    public async Task<bool> DeleteAsync(int id)
    {
        Logger.LogInformation("Deleting profile {ProfileId}", id);

        var entity = await Context.Profiles.FindAsync(id);
        if (entity is null)
        {
            Logger.LogWarning("Profile {ProfileId} not found for deletion", id);
            return false;
        }

        Context.Profiles.Remove(entity);
        await Context.SaveChangesAsync();

        Logger.LogInformation("Profile {ProfileId} deleted successfully", id);
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
