using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ScannerService.Application.Common;
using ScannerService.Application.DTOs;
using ScannerService.Domain.Common;
using ScannerService.Infrastructure.Persistence;
using ScannerService.Infrastructure.Repositories;
using Xunit;

namespace ScannerService.UnitTests.Infrastructure;

/// <summary>
/// CRUD tests for <see cref="ProfileRepository"/> against a real SQLite in-memory database.
/// Persistence is always verified through a second, change-tracker-free Context.
/// </summary>
public sealed class ProfileRepositoryTests : IClassFixture<InfrastructureSqliteFixture>
{
    private readonly InfrastructureSqliteFixture _fixture;

    public ProfileRepositoryTests(InfrastructureSqliteFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AddAsync_PersistsAllFields_RoundTripsThroughFreshContext()
    {
        string name = $"full-roundtrip-{Guid.NewGuid():N}";
        UpsertProfileDto dto = CreateFullyPopulatedDto(name);

        await using Context writeContext = _fixture.CreateContext();
        ProfileRepository writeRepository = new ProfileRepository(writeContext, NullLogger<ProfileRepository>.Instance);

        ProfileDto added = await writeRepository.AddAsync(dto);

        Assert.True(added.Id > 0);
        Assert.Equal(dto.Name, added.Name);
        Assert.Equal(dto.DeviceId, added.DeviceId);
        Assert.Equal(dto.PaperSource, added.PaperSource);
        Assert.Equal(dto.BitDepth, added.BitDepth);
        Assert.Equal(dto.PageSize, added.PageSize);
        Assert.Equal(dto.HorizontalAlign, added.HorizontalAlign);
        Assert.Equal(dto.Resolution, added.Resolution);
        Assert.Equal(dto.Scale, added.Scale);
        Assert.Equal(dto.Brightness, added.Brightness);
        Assert.Equal(dto.Contrast, added.Contrast);
        Assert.Equal(dto.ImageQuality, added.ImageQuality);
        Assert.NotEqual(default, added.CreatedAt);
        Assert.NotEqual(default, added.UpdatedAt);

        await using Context readContext = _fixture.CreateContext();
        ProfileRepository readRepository = new ProfileRepository(readContext, NullLogger<ProfileRepository>.Instance);

        ProfileDto? stored = await readRepository.GetByIdAsync(added.Id);

        Assert.NotNull(stored);
        Assert.Equal(dto.Name, stored.Name);
        Assert.Equal(dto.DeviceId, stored.DeviceId);
        Assert.Equal(dto.PaperSource, stored.PaperSource);
        Assert.Equal(dto.BitDepth, stored.BitDepth);
        Assert.Equal(dto.PageSize, stored.PageSize);
        Assert.Equal(dto.HorizontalAlign, stored.HorizontalAlign);
        Assert.Equal(dto.Resolution, stored.Resolution);
        Assert.Equal(dto.Scale, stored.Scale);
        Assert.Equal(dto.Brightness, stored.Brightness);
        Assert.Equal(dto.Contrast, stored.Contrast);
        Assert.Equal(dto.ImageQuality, stored.ImageQuality);
        Assert.Equal(added.CreatedAt, stored.CreatedAt);
        Assert.Equal(added.UpdatedAt, stored.UpdatedAt);
    }

    // KNOWN BUG F-24: pins current (buggy) behavior; flip this assertion when the bug is fixed.
    // A duplicate profile name violates the unique index and surfaces as a raw DbUpdateException
    // (rendered as HTTP 500 by the API layer) instead of a friendly Result failure.
    [Fact]
    public async Task AddAsync_DuplicateName_ThrowsDbUpdateException_CurrentBehavior()
    {
        string name = $"duplicate-{Guid.NewGuid():N}";

        await using Context firstContext = _fixture.CreateContext();
        ProfileRepository firstRepository = new ProfileRepository(firstContext, NullLogger<ProfileRepository>.Instance);
        await firstRepository.AddAsync(new UpsertProfileDto(name));

        await using Context secondContext = _fixture.CreateContext();
        ProfileRepository secondRepository = new ProfileRepository(secondContext, NullLogger<ProfileRepository>.Instance);

        await Assert.ThrowsAsync<DbUpdateException>(() => secondRepository.AddAsync(new UpsertProfileDto(name)));
    }

    [Fact]
    public async Task AddAsync_NullDeviceId_PersistsNullDeviceId()
    {
        string name = $"null-device-{Guid.NewGuid():N}";

        await using Context writeContext = _fixture.CreateContext();
        ProfileRepository writeRepository = new ProfileRepository(writeContext, NullLogger<ProfileRepository>.Instance);
        ProfileDto added = await writeRepository.AddAsync(new UpsertProfileDto(name));

        Assert.Null(added.DeviceId);

        await using Context readContext = _fixture.CreateContext();
        ProfileRepository readRepository = new ProfileRepository(readContext, NullLogger<ProfileRepository>.Instance);

        ProfileDto? stored = await readRepository.GetByIdAsync(added.Id);

        Assert.NotNull(stored);
        Assert.Null(stored.DeviceId);
    }

    [Fact]
    public async Task GetByIdAsync_ExistingId_ReturnsDto_MissingId_ReturnsNull()
    {
        string name = $"get-by-id-{Guid.NewGuid():N}";

        await using Context writeContext = _fixture.CreateContext();
        ProfileRepository writeRepository = new ProfileRepository(writeContext, NullLogger<ProfileRepository>.Instance);
        ProfileDto added = await writeRepository.AddAsync(new UpsertProfileDto(name));

        await using Context readContext = _fixture.CreateContext();
        ProfileRepository readRepository = new ProfileRepository(readContext, NullLogger<ProfileRepository>.Instance);

        ProfileDto? existing = await readRepository.GetByIdAsync(added.Id);
        ProfileDto? missing = await readRepository.GetByIdAsync(int.MaxValue);

        Assert.NotNull(existing);
        Assert.Equal(name, existing.Name);
        Assert.Null(missing);
    }

    [Fact]
    public async Task DeleteAsync_ExistingProfile_RemovesItAndReturnsSuccess()
    {
        string name = $"delete-me-{Guid.NewGuid():N}";

        await using Context writeContext = _fixture.CreateContext();
        ProfileRepository writeRepository = new ProfileRepository(writeContext, NullLogger<ProfileRepository>.Instance);
        ProfileDto added = await writeRepository.AddAsync(new UpsertProfileDto(name));

        await using Context deleteContext = _fixture.CreateContext();
        ProfileRepository deleteRepository = new ProfileRepository(deleteContext, NullLogger<ProfileRepository>.Instance);

        Result deleteResult = await deleteRepository.DeleteAsync(added.Id);

        Assert.True(deleteResult.IsSuccess);

        await using Context readContext = _fixture.CreateContext();
        ProfileRepository readRepository = new ProfileRepository(readContext, NullLogger<ProfileRepository>.Instance);

        ProfileDto? stored = await readRepository.GetByIdAsync(added.Id);

        Assert.Null(stored);
    }

    [Fact]
    public async Task DeleteAsync_MissingProfile_ReturnsFailureMentioningTheId()
    {
        await using Context context = _fixture.CreateContext();
        ProfileRepository repository = new ProfileRepository(context, NullLogger<ProfileRepository>.Instance);

        Result deleteResult = await repository.DeleteAsync(int.MaxValue);

        Assert.True(deleteResult.IsFailure);
        Assert.Contains(int.MaxValue.ToString(), deleteResult.Error);
    }

    [Fact]
    public async Task UpdateAsync_ExistingProfile_UpdatesOnlyProvidedFields()
    {
        string originalName = $"update-original-{Guid.NewGuid():N}";
        string updatedName = $"update-renamed-{Guid.NewGuid():N}";

        await using Context setupContext = _fixture.CreateContext();
        ProfileRepository setupRepository = new ProfileRepository(setupContext, NullLogger<ProfileRepository>.Instance);
        ProfileDto added = await setupRepository.AddAsync(new UpsertProfileDto(originalName));

        await using Context updateContext = _fixture.CreateContext();
        ProfileRepository updateRepository = new ProfileRepository(updateContext, NullLogger<ProfileRepository>.Instance);

        Result<ProfileDto> updateResult = await updateRepository.UpdateAsync(
            added.Id,
            new UpdateProfileDto(
                Name: updatedName,
                BitDepth: ScannerConstants.BitDepth.Grayscale,
                Resolution: 600));

        Assert.True(updateResult.IsSuccess);
        ProfileDto updated = updateResult.Value!;
        Assert.Equal(updatedName, updated.Name);
        Assert.Equal(600, updated.Resolution);
        Assert.Equal(ScannerConstants.BitDepth.Grayscale, updated.BitDepth);
        Assert.Equal(ScannerConstants.PaperSource.Glass, updated.PaperSource);
        Assert.Equal(ScannerConstants.PageSize.A4, updated.PageSize);
        Assert.Equal(ScannerConstants.HorizontalAlign.Center, updated.HorizontalAlign);
        Assert.Equal(ScannerConstants.Scale.OneToOne, updated.Scale);
        Assert.Equal(0, updated.Brightness);
        Assert.Equal(0, updated.Contrast);
        Assert.Equal(ApplicationConstants.ExportDefaults.DefaultImageQuality, updated.ImageQuality);
        Assert.True(updated.UpdatedAt >= updated.CreatedAt);

        await using Context verifyContext = _fixture.CreateContext();
        ProfileRepository verifyRepository = new ProfileRepository(verifyContext, NullLogger<ProfileRepository>.Instance);

        ProfileDto? stored = await verifyRepository.GetByIdAsync(added.Id);

        Assert.NotNull(stored);
        Assert.Equal(updatedName, stored.Name);
        Assert.Equal(600, stored.Resolution);
        Assert.Equal(ScannerConstants.BitDepth.Grayscale, stored.BitDepth);
        Assert.Equal(ScannerConstants.PageSize.A4, stored.PageSize);
    }

    [Fact]
    public async Task UpdateAsync_MissingProfile_ReturnsFailureMentioningTheId()
    {
        await using Context context = _fixture.CreateContext();
        ProfileRepository repository = new ProfileRepository(context, NullLogger<ProfileRepository>.Instance);

        Result<ProfileDto> updateResult = await repository.UpdateAsync(
            int.MaxValue,
            new UpdateProfileDto(Name: "never-persisted"));

        Assert.True(updateResult.IsFailure);
        Assert.Contains(int.MaxValue.ToString(), updateResult.Error);
        Assert.Null(updateResult.Value);
    }

    [Fact]
    public async Task GetAllAsync_ContainsEveryAddedProfile()
    {
        string firstName = $"list-all-first-{Guid.NewGuid():N}";
        string secondName = $"list-all-second-{Guid.NewGuid():N}";

        await using Context writeContext = _fixture.CreateContext();
        ProfileRepository writeRepository = new ProfileRepository(writeContext, NullLogger<ProfileRepository>.Instance);
        await writeRepository.AddAsync(new UpsertProfileDto(firstName));
        await writeRepository.AddAsync(new UpsertProfileDto(secondName));

        await using Context readContext = _fixture.CreateContext();
        ProfileRepository readRepository = new ProfileRepository(readContext, NullLogger<ProfileRepository>.Instance);

        List<ProfileDto> all = await readRepository.GetAllAsync();
        List<string> names = all.Select(profile => profile.Name).ToList();

        Assert.Contains(firstName, names);
        Assert.Contains(secondName, names);
    }

    private static UpsertProfileDto CreateFullyPopulatedDto(string name)
    {
        return new UpsertProfileDto(
            Name: name,
            DeviceId: "device-full-roundtrip",
            PaperSource: ScannerConstants.PaperSource.Feeder,
            BitDepth: ScannerConstants.BitDepth.Grayscale,
            PageSize: ScannerConstants.PageSize.Legal,
            HorizontalAlign: ScannerConstants.HorizontalAlign.Right,
            Resolution: 300,
            Scale: ScannerConstants.Scale.HalfSize,
            Brightness: 12,
            Contrast: -7,
            ImageQuality: 90);
    }
}
