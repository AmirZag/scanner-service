using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScannerService.Application.DTOs;
using ScannerService.Application.Interfaces;
using ScannerService.Domain.Entities;
using ScannerService.Infrastructure.Persistence;

namespace ScannerService.Infrastructure.Repositories;

public class ExportSettingRepository : IExportSettingRepository
{
    private readonly Context _context;
    private readonly ILogger<ExportSettingRepository> _logger;

    public ExportSettingRepository(
        Context context,
        ILogger<ExportSettingRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ExportSettingDto> GetExportSettingAsync()
    {
        _logger.LogDebug("Retrieving export settings");
        var entity = await GetExportSettingEntityAsync();
        return new ExportSettingDto(entity.Format, entity.ExportPath, entity.FileName);
    }

    public async Task UpdateExportSettingAsync(ExportSettingDto exportSettingDto)
    {
        _logger.LogInformation("Updating export settings");
        var entity = await GetExportSettingEntityAsync();

        if (exportSettingDto.Format != null)
        {
            entity.Format = exportSettingDto.Format;
        }
        if (exportSettingDto.ExportPath != null)
        {
            entity.ExportPath = exportSettingDto.ExportPath;
        }
        if (exportSettingDto.FileName != null)
        {
            entity.FileName = exportSettingDto.FileName;
        }

        entity.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Export settings updated successfully");
    }

    public async Task<ExportSetting> GetExportSettingEntityAsync()
    {
        var entity = await _context.ExportSettings.FirstOrDefaultAsync();

        if (entity is null)
        {
            _logger.LogInformation("No export setting found, Creating default");

            entity = new ExportSetting
            {
                Format = "PDF",
                ExportPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Scans")
            };

            _context.ExportSettings.Add(entity);
            await _context.SaveChangesAsync();
        }
        return entity;
    }
}
