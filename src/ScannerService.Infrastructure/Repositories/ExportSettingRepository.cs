using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScannerService.Application.Common;
using ScannerService.Application.DTOs;
using ScannerService.Application.Interfaces;
using ScannerService.Domain.Common;
using ScannerService.Domain.Entities;
using ScannerService.Infrastructure.Persistence;

namespace ScannerService.Infrastructure.Repositories;

public class ExportSettingRepository : RepositoryBase<ExportSetting>, IExportSettingRepository
{
    public ExportSettingRepository(
        Context context,
        ILogger<ExportSettingRepository> logger)
        : base(context, logger)
    {
    }

    public async Task<Result<ExportSettingDto>> GetExportSettingAsync(CancellationToken cancellationToken = default)
    {
        Logger.LogDebug("Retrieving export settings");
        try
        {
            var entity = await GetExportSettingEntityAsync(cancellationToken);

            // Log current values for debugging
            Logger.LogDebug("Current export settings - Format: '{Format}', FileName: '{FileName}'", entity.Format, entity.FileName);

            // Check if sanitization is needed
            var needsSanitization = RequiresSanitization(entity);
            Logger.LogDebug("Export settings require sanitization: {NeedsSanitization}", needsSanitization);

            // Sanitize and validate the entity before returning
            var sanitizedDto = SanitizeExportSetting(entity);
            Logger.LogDebug("Sanitized export settings - Format: '{Format}', FileName: '{FileName}'", sanitizedDto.Format, sanitizedDto.FileName);

            // If data was sanitized, update the database
            if (needsSanitization)
            {
                Logger.LogInformation("Sanitizing invalid export settings in database");
                entity.Update(
                    sanitizedDto.Format,
                    sanitizedDto.ExportPath,
                    sanitizedDto.FileName
                );
                await Context.SaveChangesAsync(cancellationToken);
                Logger.LogInformation("Export settings sanitized and saved to database");
            }

            return Result<ExportSettingDto>.Success(sanitizedDto);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to retrieve export settings");
            return Result<ExportSettingDto>.Failure($"Failed to retrieve export settings: {ex.Message}");
        }
    }

    public async Task<Result> UpdateExportSettingAsync(ExportSettingDto exportSettingDto, CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("Updating export settings");
        try
        {
            var entity = await GetExportSettingEntityAsync(cancellationToken);

            // Sanitize input before updating
            var sanitizedDto = SanitizeDto(exportSettingDto);

            entity.Update(
                sanitizedDto.Format,
                sanitizedDto.ExportPath,
                sanitizedDto.FileName
            );

            await Context.SaveChangesAsync(cancellationToken);

            Logger.LogInformation("Export settings updated successfully");
            return Result.Success();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to update export settings");
            return Result.Failure($"Failed to update export settings: {ex.Message}");
        }
    }

    private async Task<ExportSetting> GetExportSettingEntityAsync(CancellationToken cancellationToken = default)
    {
        var entity = await Context.ExportSettings.FirstOrDefaultAsync(cancellationToken);

        if (entity is null)
        {
            Logger.LogInformation("No export setting found, Creating default");

            entity = ExportSetting.CreateDefault();
            entity.ExportPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Scans");

            Context.ExportSettings.Add(entity);
            await Context.SaveChangesAsync(cancellationToken);
        }
        return entity;
    }

    /// <summary>
    /// Sanitizes an ExportSetting entity to ensure valid values.
    /// </summary>
    private ExportSettingDto SanitizeExportSetting(ExportSetting entity)
    {
        var format = string.IsNullOrWhiteSpace(entity.Format)
            ? ApplicationConstants.ExportDefaults.DefaultFormat
            : entity.Format.Trim();

        var fileName = string.IsNullOrWhiteSpace(entity.FileName)
            ? ApplicationConstants.ExportDefaults.DefaultFileName
            : entity.FileName.Trim();

        var exportPath = string.IsNullOrWhiteSpace(entity.ExportPath)
            ? string.Empty
            : entity.ExportPath.Trim();

        return new ExportSettingDto(format, exportPath, fileName);
    }

    /// <summary>
    /// Sanitizes an ExportSettingDto to ensure valid values.
    /// </summary>
    private ExportSettingDto SanitizeDto(ExportSettingDto dto)
    {
        var format = string.IsNullOrWhiteSpace(dto.Format)
            ? ApplicationConstants.ExportDefaults.DefaultFormat
            : dto.Format.Trim();

        var fileName = string.IsNullOrWhiteSpace(dto.FileName)
            ? ApplicationConstants.ExportDefaults.DefaultFileName
            : dto.FileName.Trim();

        var exportPath = string.IsNullOrWhiteSpace(dto.ExportPath)
            ? string.Empty
            : dto.ExportPath.Trim();

        return new ExportSettingDto(format, exportPath, fileName);
    }

    /// <summary>
    /// Checks if an entity requires sanitization.
    /// </summary>
    private bool RequiresSanitization(ExportSetting entity)
    {
        if (string.IsNullOrWhiteSpace(entity.Format) || entity.Format.Trim() != entity.Format)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(entity.FileName) || entity.FileName.Trim() != entity.FileName)
        {
            return true;
        }

        if (entity.ExportPath != null && entity.ExportPath.Trim() != entity.ExportPath)
        {
            return true;
        }

        return false;
    }
}
