using System;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ScannerService.Domain.Common;
using ScannerService.Domain.Entities;
using ScannerService.Infrastructure.Persistence;
using Xunit;

namespace ScannerService.UnitTests.Infrastructure;

/// <summary>
/// Tripwire tests for the EF Core model of <see cref="Context"/>: the EnsureCreated seed row
/// for ExportSetting, the idempotence of EnsureCreated, and DateTime persistence fidelity.
/// This class never mutates the ExportSettings table, so the seed assertions stay order-proof.
/// </summary>
public sealed class ContextModelTests : IClassFixture<InfrastructureSqliteFixture>
{
    private readonly InfrastructureSqliteFixture _fixture;

    public ContextModelTests(InfrastructureSqliteFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task EnsureCreated_SeedsExportSetting_WithId1AndEmptyExportPath()
    {
        await using Context context = _fixture.CreateContext();

        ExportSetting seeded = await context.ExportSettings.SingleAsync();

        Assert.Equal(1, seeded.Id);
        Assert.Equal(ScannerConstants.ExportFormat.PDF, seeded.Format);
        Assert.Equal(string.Empty, seeded.ExportPath);
        Assert.Equal(ApplicationConstants.ExportDefaults.DefaultFileName, seeded.FileName);
    }

    [Fact]
    public async Task EnsureCreated_SecondCall_ReturnsFalseWithoutAlteringSchema()
    {
        await using Context context = _fixture.CreateContext();

        bool created = await context.Database.EnsureCreatedAsync();

        Assert.False(created);
    }

    [Fact]
    public async Task Persistence_DateTimeUtc_RoundTripsSameInstantAsIsoText()
    {
        DateTime written = new DateTime(2026, 10, 3, 11, 22, 33, 123, DateTimeKind.Utc);
        string profileName = $"utc-roundtrip-{Guid.NewGuid():N}";

        await using (Context writeContext = _fixture.CreateContext())
        {
            writeContext.Profiles.Add(new Profile
            {
                Name = profileName,
                DeviceId = "device-utc",
                CreatedAt = written,
                UpdatedAt = written
            });
            await writeContext.SaveChangesAsync();
        }

        await using (Context readContext = _fixture.CreateContext())
        {
            Profile stored = await readContext.Profiles.SingleAsync(candidate => candidate.Name == profileName);
            Assert.Equal(written, stored.CreatedAt);
            Assert.Equal(written, stored.UpdatedAt);
        }

        await using Context rawContext = _fixture.CreateContext();
        DbConnection connection = rawContext.Database.GetDbConnection();
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT \"CreatedAt\" FROM \"Profiles\" WHERE \"Name\" = @name";
        DbParameter nameParameter = command.CreateParameter();
        nameParameter.ParameterName = "@name";
        nameParameter.Value = profileName;
        _ = command.Parameters.Add(nameParameter);
        object? rawValue = await command.ExecuteScalarAsync();

        Assert.NotNull(rawValue);
        Assert.Matches("^\\d{4}-\\d{2}-\\d{2}", Assert.IsType<string>(rawValue));
    }
}
