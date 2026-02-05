using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScannerService.Application.DTOs;
using ScannerService.Application.Interfaces;
using ScannerService.Domain.Entities;
using ScannerService.Infrastructure.Persistence;
using System;
using System.IO;
using System.Threading.Tasks;

namespace ScannerService.Infrastructure.Services;

public class ExportSettingsService : IExportSettings
{
    private readonly ScannerDbContext _context;
    private readonly ILogger<ExportSettingsService> _logger;

    public ExportSettingsService(ScannerDbContext context, ILogger<ExportSettingsService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ExportSettingsDto> GetExportSettingsAsync()
    {
        _logger.LogDebug("Retrieving export settings");
        var settings = await GetExportSettingsInternalAsync();
        return new ExportSettingsDto(settings.OutputFormat, settings.OutputDirectory, settings.FileName);
    }

    public async Task UpdateExportSettingsAsync(ExportSettingsDto dto)
    {
        _logger.LogInformation("Updating export settings");
        var settings = await GetExportSettingsInternalAsync();

        if (dto.OutputFormat != null) settings.OutputFormat = dto.OutputFormat;
        if (dto.OutputDirectory != null) settings.OutputDirectory = dto.OutputDirectory;
        if (dto.FileName != null) settings.FileName = dto.FileName;

        settings.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        _logger.LogInformation("Export settings updated successfully");
    }

    private async Task<ExportSettings> GetExportSettingsInternalAsync()
    {
        var settings = await _context.ExportSettings.FirstOrDefaultAsync();
        if (settings == null)
        {
            _logger.LogInformation("No export settings found, creating default");
            settings = new ExportSettings
            {
                OutputFormat = "PDF",
                OutputDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Scans")
            };
            _context.ExportSettings.Add(settings);
            await _context.SaveChangesAsync();
        }
        return settings;
    }
}