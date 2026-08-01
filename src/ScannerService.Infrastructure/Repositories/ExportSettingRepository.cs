using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScannerService.Application.DTOs;
using ScannerService.Application.Interfaces;
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

    public async Task<ExportSettingDto> GetExportSettingAsync(CancellationToken cancellationToken = default)
    {
        Logger.LogDebug("Retrieving export settings");
        var entity = await GetExportSettingEntityAsync(cancellationToken);
        return new ExportSettingDto(entity.Format, entity.ExportPath, entity.FileName);
    }

    public async Task UpdateExportSettingAsync(ExportSettingDto exportSettingDto, CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("Updating export settings");
        var entity = await GetExportSettingEntityAsync(cancellationToken);

        entity.Update(
            exportSettingDto.Format,
            exportSettingDto.ExportPath,
            exportSettingDto.FileName
        );

        await Context.SaveChangesAsync(cancellationToken);

        Logger.LogInformation("Export settings updated successfully");
    }

    public async Task<ExportSetting> GetExportSettingEntityAsync(CancellationToken cancellationToken = default)
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
}
