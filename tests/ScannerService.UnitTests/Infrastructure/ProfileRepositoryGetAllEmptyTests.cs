using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ScannerService.Application.DTOs;
using ScannerService.Infrastructure.Persistence;
using ScannerService.Infrastructure.Repositories;
using Xunit;

namespace ScannerService.UnitTests.Infrastructure;

/// <summary>
/// Uses its own empty fixture so the Profiles table is guaranteed to hold no rows,
/// keeping the exact-empty assertion independent of every other test class.
/// </summary>
public sealed class ProfileRepositoryGetAllEmptyTests : IClassFixture<InfrastructureSqliteFixture>
{
    private readonly InfrastructureSqliteFixture _fixture;

    public ProfileRepositoryGetAllEmptyTests(InfrastructureSqliteFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetAllAsync_EmptyProfilesTable_ReturnsEmptyList()
    {
        await using Context context = _fixture.CreateContext();
        ProfileRepository repository = new ProfileRepository(context, NullLogger<ProfileRepository>.Instance);

        List<ProfileDto> profiles = await repository.GetAllAsync();

        Assert.Empty(profiles);
    }
}
