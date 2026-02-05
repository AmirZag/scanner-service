using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScannerService.Application.DTOs;
using ScannerService.Application.Interfaces;
using ScannerService.Domain.Entities;
using ScannerService.Infrastructure.Persistence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace ScannerService.Infrastructure.Repositories;

public class ProfileRepository : IProfileRepository
{
    private readonly ScannerDbContext _context;
    private readonly ILogger<ProfileRepository> _logger;

    public ProfileRepository(ScannerDbContext context, ILogger<ProfileRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<ProfileDto>> GetAllAsync()
    {
        _logger.LogDebug("Retrieving all profiles");
        return await _context.Profiles.Select(DtoProjection).ToListAsync();
    }

    public async Task<ProfileDto?> GetByIdAsync(int id)
    {
        _logger.LogDebug("Retrieving profile {ProfileId}", id);
        return await _context.Profiles.Where(x => x.Id == id).Select(DtoProjection).FirstOrDefaultAsync();
    }

    public async Task<ProfileDto> AddAsync(UpsertProfile req)
    {
        _logger.LogInformation("Adding profile {ProfileName}", req.Name);

        var entity = new Profile
        {
            Name = req.Name,
            DeviceId = req.DeviceId,
            PaperSource = req.PaperSource,
            BitDepth = req.BitDepth,
            PageSize = req.PageSize,
            HorizontalAlign = req.HorizontalAlign,
            Resolution = req.Resolution,
            Scale = req.Scale,
            Brightness = req.Brightness,
            Contrast = req.Contrast,
            ImageQuality = req.ImageQuality,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Profiles.Add(entity);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Profile added with ID {ProfileId}", entity.Id);
        return ToDto(entity);
    }

    public async Task<ProfileDto?> UpdateAsync(int id, UpsertProfile req)
    {
        _logger.LogInformation("Updating profile {ProfileId}", id);

        var entity = await _context.Profiles.FindAsync(id);
        if (entity is null)
        {
            _logger.LogWarning("Profile {ProfileId} not found for update", id);
            return null;
        }

        entity.Name = req.Name;
        entity.DeviceId = req.DeviceId;
        entity.PaperSource = req.PaperSource;
        entity.BitDepth = req.BitDepth;
        entity.PageSize = req.PageSize;
        entity.HorizontalAlign = req.HorizontalAlign;
        entity.Resolution = req.Resolution;
        entity.Scale = req.Scale;
        entity.Brightness = req.Brightness;
        entity.Contrast = req.Contrast;
        entity.ImageQuality = req.ImageQuality;
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

    private static readonly Expression<Func<Profile, ProfileDto>> DtoProjection = p => new ProfileDto(
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