using FluentValidation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ScannerService.Application.DTOs;
using ScannerService.Application.Interfaces;
using ScannerService.Application.Validators;
using ScannerService.Infrastructure.Persistence;
using ScannerService.TrayApp;
using ScannerService.TrayApp.Configurations;

namespace ScannerService.UnitTests.ApiIntegration;

/// <summary>
/// Route-compilation fallbacks for the shared endpoint map. ConfigureAllEndpoints maps every
/// endpoint group on one WebApplication, so a test class that exercises only one group still
/// needs every service the map resolves: a missing registration makes minimal-API infer that
/// parameter from the request body and the whole app fails to compile its routes ("body was
/// inferred but the method does not allow inferred body parameters"). These defaults are
/// registered with TryAdd* so a class's own registrations always win; their methods throw if a
/// mis-configured test actually reaches them, which is the diagnostic we want.
/// </summary>
internal static class DefaultEndpointDependencies
{
    public static void Register(IServiceCollection services)
    {
        SqliteConnection fallbackConnection = new SqliteConnection($"Data Source=memdb_{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        fallbackConnection.Open();
        services.TryAddSingleton(fallbackConnection);
        services.TryAddSingleton<Context>(static sp => new Context(
            new DbContextOptionsBuilder<Context>().UseSqlite(sp.GetRequiredService<SqliteConnection>()).Options));

        services.TryAddSingleton<IScannerQueries>(new FakeScannerQueries(new List<ScannerDto>()));
        services.TryAddSingleton<IScannerListCache>(new FakeScannerListCache());
        services.TryAddSingleton<IScanJobService>(new FakeScanJobService());
        services.TryAddSingleton<IRecentScansService>(new FakeRecentScansService());
        services.TryAddSingleton<IProfileRepository>(new FakeProfileRepository());
        services.TryAddSingleton<IExportSettingRepository>(new FakeExportSettingRepository());

        services.TryAddSingleton(new ScannerServiceConfiguration());
        services.TryAddSingleton<LocalSettingsStore>(static sp => new LocalSettingsStore(sp.GetRequiredService<ScannerServiceConfiguration>()));
        services.TryAddSingleton<IApiHostControl>(static sp => new ApiHostControl(new WebApiHostService(
            sp.GetRequiredService<ScannerServiceConfiguration>(),
            sp.GetRequiredService<LocalSettingsStore>())));

        services.TryAddSingleton<IValidator<UpsertProfileDto>>(new UpsertProfileValidator());
        services.TryAddSingleton<IValidator<UpdateProfileDto>>(new UpdateProfileValidator());
        services.TryAddSingleton<IValidator<ScanRequestDto>>(new ScanRequestValidator());
        services.TryAddSingleton<IValidator<ExportSettingDto>>(new ExportSettingValidator());
        services.TryAddSingleton<IValidator<ScannerSettingsDto>>(new ScannerSettingsValidator());
    }
}
