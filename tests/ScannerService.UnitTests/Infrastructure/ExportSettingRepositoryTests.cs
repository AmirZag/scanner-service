using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ScannerService.Application.Common;
using ScannerService.Application.DTOs;
using ScannerService.Domain.Common;
using ScannerService.Domain.Entities;
using ScannerService.Infrastructure.Persistence;
using ScannerService.Infrastructure.Repositories;
using Xunit;

namespace ScannerService.UnitTests.Infrastructure;

/// <summary>
/// Tests for <see cref="ExportSettingRepository"/>: seed-row reads, full-field updates,
/// input sanitization, sanitize-on-read with write-back, and the create-default fallback
/// for a table whose seed row was deleted. Every test reseeds the singleton row first so
/// the tests stay independent of execution order.
/// </summary>
public sealed class ExportSettingRepositoryTests : IClassFixture<InfrastructureSqliteFixture>
{
    private readonly InfrastructureSqliteFixture _fixture;

    public ExportSettingRepositoryTests(InfrastructureSqliteFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetExportSettingAsync_SeededRow_ReturnsDefaults()
    {
        await ReseedAsync();

        await using Context context = _fixture.CreateContext();
        ExportSettingRepository repository = new ExportSettingRepository(context, NullLogger<ExportSettingRepository>.Instance);

        Result<ExportSettingDto> result = await repository.GetExportSettingAsync();

        Assert.True(result.IsSuccess);
        ExportSettingDto dto = result.Value!;
        Assert.Equal(ScannerConstants.ExportFormat.PDF, dto.Format);
        Assert.Equal(string.Empty, dto.ExportPath);
        Assert.Equal(ApplicationConstants.ExportDefaults.DefaultFileName, dto.FileName);
    }

    [Fact]
    public async Task UpdateExportSettingAsync_PersistsEveryField()
    {
        await ReseedAsync();

        await using Context writeContext = _fixture.CreateContext();
        ExportSettingRepository writeRepository = new ExportSettingRepository(writeContext, NullLogger<ExportSettingRepository>.Instance);

        ExportSettingDto update = new ExportSettingDto(
            ScannerConstants.ExportFormat.PNG,
            @"C:\Scans\Exports",
            "report_{datetime}");
        Result updateResult = await writeRepository.UpdateExportSettingAsync(update);

        Assert.True(updateResult.IsSuccess);

        await using Context verifyContext = _fixture.CreateContext();
        ExportSetting stored = await verifyContext.ExportSettings.SingleAsync();

        Assert.Equal(ScannerConstants.ExportFormat.PNG, stored.Format);
        Assert.Equal(@"C:\Scans\Exports", stored.ExportPath);
        Assert.Equal("report_{datetime}", stored.FileName);
        Assert.NotEqual(default, stored.UpdatedAt);
    }

    [Fact]
    public async Task UpdateExportSettingAsync_SanitizesInputValues()
    {
        await ReseedAsync();

        await using Context writeContext = _fixture.CreateContext();
        ExportSettingRepository writeRepository = new ExportSettingRepository(writeContext, NullLogger<ExportSettingRepository>.Instance);

        ExportSettingDto padded = new ExportSettingDto(
            " PNG ",
            "   ",
            " report_{datetime} ");
        Result updateResult = await writeRepository.UpdateExportSettingAsync(padded);

        Assert.True(updateResult.IsSuccess);

        await using Context verifyContext = _fixture.CreateContext();
        ExportSetting stored = await verifyContext.ExportSettings.SingleAsync();

        Assert.Equal(ScannerConstants.ExportFormat.PNG, stored.Format);
        Assert.Equal(string.Empty, stored.ExportPath);
        Assert.Equal("report_{datetime}", stored.FileName);
    }

    [Fact]
    public async Task GetExportSettingAsync_PaddedStoredValues_SanitizesAndWritesBack()
    {
        await ReseedAsync();
        await SetRawRowValuesAsync(" PDF ", " C:\\Scans ", " report_{datetime} ");

        await using Context readContext = _fixture.CreateContext();
        ExportSettingRepository repository = new ExportSettingRepository(readContext, NullLogger<ExportSettingRepository>.Instance);

        Result<ExportSettingDto> result = await repository.GetExportSettingAsync();

        Assert.True(result.IsSuccess);
        ExportSettingDto dto = result.Value!;
        Assert.Equal(ScannerConstants.ExportFormat.PDF, dto.Format);
        Assert.Equal(@"C:\Scans", dto.ExportPath);
        Assert.Equal("report_{datetime}", dto.FileName);

        await using Context verifyContext = _fixture.CreateContext();
        ExportSetting stored = await verifyContext.ExportSettings.SingleAsync();

        Assert.Equal(ScannerConstants.ExportFormat.PDF, stored.Format);
        Assert.Equal(@"C:\Scans", stored.ExportPath);
        Assert.Equal("report_{datetime}", stored.FileName);
    }

    [Fact]
    public async Task GetExportSettingAsync_EmptyStoredFileName_SanitizesToDefaultAndWritesBack()
    {
        await ReseedAsync();
        await SetRawRowValuesAsync(ScannerConstants.ExportFormat.PDF, string.Empty, string.Empty);

        await using Context readContext = _fixture.CreateContext();
        ExportSettingRepository repository = new ExportSettingRepository(readContext, NullLogger<ExportSettingRepository>.Instance);

        Result<ExportSettingDto> result = await repository.GetExportSettingAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(ApplicationConstants.ExportDefaults.DefaultFileName, result.Value!.FileName);

        await using Context verifyContext = _fixture.CreateContext();
        ExportSetting stored = await verifyContext.ExportSettings.SingleAsync();

        Assert.Equal(ApplicationConstants.ExportDefaults.DefaultFileName, stored.FileName);
        Assert.Equal(ScannerConstants.ExportFormat.PDF, stored.Format);
    }

    [Fact]
    public async Task GetExportSettingAsync_MissingRow_CreatesDefaultAtDocumentsPath()
    {
        await DeleteAllRowsAsync();

        await using Context context = _fixture.CreateContext();
        ExportSettingRepository repository = new ExportSettingRepository(context, NullLogger<ExportSettingRepository>.Instance);

        Result<ExportSettingDto> result = await repository.GetExportSettingAsync();

        Assert.True(result.IsSuccess);
        ExportSettingDto dto = result.Value!;
        Assert.Equal(ScannerConstants.ExportFormat.PDF, dto.Format);
        Assert.Equal(ApplicationConstants.ExportDefaults.DefaultFileName, dto.FileName);
        string expectedPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Scans");
        Assert.Equal(expectedPath, dto.ExportPath);

        await using Context verifyContext = _fixture.CreateContext();
        ExportSetting stored = await verifyContext.ExportSettings.SingleAsync();

        Assert.Equal(expectedPath, stored.ExportPath);
    }

    [Fact]
    public async Task UpdateExportSettingAsync_MissingRow_CreatesRowWithSanitizedValues()
    {
        await DeleteAllRowsAsync();

        await using Context writeContext = _fixture.CreateContext();
        ExportSettingRepository writeRepository = new ExportSettingRepository(writeContext, NullLogger<ExportSettingRepository>.Instance);

        ExportSettingDto update = new ExportSettingDto(
            " TIFF ",
            string.Empty,
            string.Empty);
        Result updateResult = await writeRepository.UpdateExportSettingAsync(update);

        Assert.True(updateResult.IsSuccess);

        await using Context verifyContext = _fixture.CreateContext();
        ExportSetting stored = await verifyContext.ExportSettings.SingleAsync();

        Assert.Equal(ScannerConstants.ExportFormat.TIFF, stored.Format);
        Assert.Equal(string.Empty, stored.ExportPath);
        Assert.Equal(ApplicationConstants.ExportDefaults.DefaultFileName, stored.FileName);
    }

    [Fact]
    public async Task GetExportSettingAsync_DatabaseQueryFails_ReturnsFailureWithMessage()
    {
        // A context over a fresh :memory: connection has no schema, so the first query throws;
        // the repository must convert the exception into a Result failure instead.
        await using SqliteConnection connection = new SqliteConnection("Data Source=:memory:");
        await using Context context = CreateContextWithoutSchema(connection);
        ExportSettingRepository repository = new ExportSettingRepository(context, NullLogger<ExportSettingRepository>.Instance);

        Result<ExportSettingDto> result = await repository.GetExportSettingAsync();

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        Assert.StartsWith("Failed to retrieve export settings:", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateExportSettingAsync_DatabaseQueryFails_ReturnsFailureWithMessage()
    {
        await using SqliteConnection connection = new SqliteConnection("Data Source=:memory:");
        await using Context context = CreateContextWithoutSchema(connection);
        ExportSettingRepository repository = new ExportSettingRepository(context, NullLogger<ExportSettingRepository>.Instance);

        Result result = await repository.UpdateExportSettingAsync(
            new ExportSettingDto(ScannerConstants.ExportFormat.PDF, @"C:\Scans\Exports", "scan_{datetime}"));

        Assert.True(result.IsFailure);
        Assert.StartsWith("Failed to update export settings:", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetExportSettingAsync_PaddedExportPathOnly_SanitizesPathAndWritesBack()
    {
        // Format and file name are clean, so only the ExportPath check can demand sanitization;
        // this walks RequiresSanitization down to its ExportPath branch.
        await ReseedAsync();
        await SetRawRowValuesAsync(
            ScannerConstants.ExportFormat.PDF,
            " C:\\Scans\\Exports ",
            ApplicationConstants.ExportDefaults.DefaultFileName);

        await using Context readContext = _fixture.CreateContext();
        ExportSettingRepository repository = new ExportSettingRepository(readContext, NullLogger<ExportSettingRepository>.Instance);

        Result<ExportSettingDto> result = await repository.GetExportSettingAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(@"C:\Scans\Exports", result.Value!.ExportPath);

        await using Context verifyContext = _fixture.CreateContext();
        ExportSetting stored = await verifyContext.ExportSettings.SingleAsync();

        Assert.Equal(@"C:\Scans\Exports", stored.ExportPath);
        Assert.Equal(ScannerConstants.ExportFormat.PDF, stored.Format);
        Assert.Equal(ApplicationConstants.ExportDefaults.DefaultFileName, stored.FileName);
    }

    /// <summary>
    /// Builds a Context over a connection to a never-initialized :memory: database, so every
    /// EF call fails (no schema) and the repository fault paths can run without disturbing
    /// the shared class fixture.
    /// </summary>
    private static Context CreateContextWithoutSchema(SqliteConnection connection)
    {
        DbContextOptionsBuilder<Context> optionsBuilder = new DbContextOptionsBuilder<Context>();
        optionsBuilder.UseSqlite(connection);
        return new Context(optionsBuilder.Options);
    }

    /// <summary>
    /// Replaces the singleton row with the canonical seed values so each test starts
    /// from a pristine, order-independent state.
    /// </summary>
    private async Task ReseedAsync()
    {
        await using Context context = _fixture.CreateContext();
        await context.ExportSettings.ExecuteDeleteAsync();
        context.ExportSettings.Add(new ExportSetting
        {
            Id = 1,
            Format = ScannerConstants.ExportFormat.PDF,
            ExportPath = string.Empty,
            FileName = ApplicationConstants.ExportDefaults.DefaultFileName
        });
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Writes raw (unsanitized) values into the singleton row through a plain Context,
    /// bypassing the repository, to simulate legacy or externally corrupted data.
    /// </summary>
    private async Task SetRawRowValuesAsync(string format, string exportPath, string fileName)
    {
        await using Context context = _fixture.CreateContext();
        ExportSetting entity = await context.ExportSettings.SingleAsync();
        entity.Format = format;
        entity.ExportPath = exportPath;
        entity.FileName = fileName;
        await context.SaveChangesAsync();
    }

    private async Task DeleteAllRowsAsync()
    {
        await using Context context = _fixture.CreateContext();
        await context.ExportSettings.ExecuteDeleteAsync();
    }
}
