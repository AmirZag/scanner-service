using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ScannerService.Domain.Entities;
using ScannerService.Infrastructure.Persistence;
using ScannerService.Infrastructure.Repositories;
using Xunit;

namespace ScannerService.UnitTests.Infrastructure;

/// <summary>
/// Exercises every <see cref="RepositoryBase{T}"/> helper through <see cref="RepositoryBaseHarness"/>
/// against a real SQLite in-memory database. Each test seeds rows under a unique prefix and asserts
/// only on its own rows, so tests are independent of execution order.
/// </summary>
public sealed class RepositoryBaseTests : IClassFixture<InfrastructureSqliteFixture>
{
    private readonly InfrastructureSqliteFixture _fixture;

    public RepositoryBaseTests(InfrastructureSqliteFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task EntityExistsAsync_TrueForSeededId_FalseForMissingId()
    {
        List<int> ids = await SeedProfilesAsync("entity-exists");

        await using Context context = _fixture.CreateContext();
        RepositoryBaseHarness harness = CreateHarness(context);

        bool exists = await harness.EntityExists(ids[0]);
        bool missing = await harness.EntityExists(int.MaxValue);

        Assert.True(exists);
        Assert.False(missing);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsSeededEntity_AndNullForMissingId()
    {
        List<int> ids = await SeedProfilesAsync("get-by-id");

        await using Context context = _fixture.CreateContext();
        RepositoryBaseHarness harness = CreateHarness(context);

        Profile? existing = await harness.GetById(ids[0]);
        Profile? missing = await harness.GetById(int.MaxValue);

        Assert.NotNull(existing);
        Assert.Equal("get-by-id-alpha", existing.Name);
        Assert.Null(missing);
    }

    [Fact]
    public async Task GetByIdWithToken_ReturnsSeededEntity()
    {
        List<int> ids = await SeedProfilesAsync("get-by-id-token");

        await using Context context = _fixture.CreateContext();
        RepositoryBaseHarness harness = CreateHarness(context);

        Profile? existing = await harness.GetByIdWithToken(ids[1], CancellationToken.None);

        Assert.NotNull(existing);
        Assert.Equal("get-by-id-token-bravo", existing.Name);
    }

    [Fact]
    public async Task ExistsAsync_TrueWhenPredicateMatches_FalseOtherwise()
    {
        await SeedProfilesAsync("exists-predicate");

        await using Context context = _fixture.CreateContext();
        RepositoryBaseHarness harness = CreateHarness(context);

        bool match = await harness.Exists(profile => profile.Name == "exists-predicate-bravo");
        bool miss = await harness.Exists(profile => profile.Name == "exists-predicate-zulu");

        Assert.True(match);
        Assert.False(miss);
    }

    [Fact]
    public async Task CountAsync_WithPredicate_CountsOnlyMatchingRows()
    {
        await SeedProfilesAsync("count-predicate");

        await using Context context = _fixture.CreateContext();
        RepositoryBaseHarness harness = CreateHarness(context);

        int count = await harness.Count(profile =>
            profile.Name == "count-predicate-alpha" || profile.Name == "count-predicate-charlie");
        int total = await harness.Count();

        Assert.Equal(2, count);
        Assert.True(total >= 3);
    }

    [Fact]
    public async Task ListAsync_WithPredicate_ReturnsOnlyMatchingRows()
    {
        await SeedProfilesAsync("list-predicate");

        await using Context context = _fixture.CreateContext();
        RepositoryBaseHarness harness = CreateHarness(context);

        List<Profile> matches = await harness.List(profile =>
            profile.Name == "list-predicate-alpha" || profile.Name == "list-predicate-charlie");
        List<Profile> noMatches = await harness.List(profile => profile.Name == "list-predicate-zulu");

        Assert.Equal(2, matches.Count);
        Assert.All(matches, profile => Assert.StartsWith("list-predicate-", profile.Name));
        Assert.Empty(noMatches);
    }

    [Fact]
    public async Task ListAsync_WithoutPredicate_ContainsEverySeededName()
    {
        await SeedProfilesAsync("list-all");

        await using Context context = _fixture.CreateContext();
        RepositoryBaseHarness harness = CreateHarness(context);

        List<Profile> all = await harness.List();
        List<string> names = all.Select(profile => profile.Name).ToList();

        Assert.Contains("list-all-alpha", names);
        Assert.Contains("list-all-bravo", names);
        Assert.Contains("list-all-charlie", names);
    }

    [Fact]
    public async Task PaginatedAsync_FirstPage_ReturnsMetadataAndPageCount()
    {
        await SeedProfilesAsync("paginated-first");
        await using Context context = _fixture.CreateContext();
        RepositoryBaseHarness harness = CreateHarness(context);
        Expression<Func<Profile, bool>> filter = profile =>
            profile.Name == "paginated-first-alpha"
            || profile.Name == "paginated-first-bravo"
            || profile.Name == "paginated-first-charlie";

        PaginatedResult<Profile> page = await harness.Paginated(1, 2, filter);

        Assert.Equal(3, page.TotalCount);
        Assert.Equal(1, page.Page);
        Assert.Equal(2, page.PageSize);
        Assert.Equal(2, page.TotalPages);
        Assert.False(page.HasPreviousPage);
        Assert.True(page.HasNextPage);
        Assert.Equal(2, page.Items.Count);
    }

    [Fact]
    public async Task PaginatedAsync_BothPagesTogether_CoverEveryMatchingRow()
    {
        await SeedProfilesAsync("paginated-union");
        await using Context context = _fixture.CreateContext();
        RepositoryBaseHarness harness = CreateHarness(context);
        Expression<Func<Profile, bool>> filter = profile =>
            profile.Name == "paginated-union-alpha"
            || profile.Name == "paginated-union-bravo"
            || profile.Name == "paginated-union-charlie";

        PaginatedResult<Profile> firstPage = await harness.Paginated(1, 2, filter);
        PaginatedResult<Profile> lastPage = await harness.Paginated(2, 2, filter);

        Assert.Single(lastPage.Items);
        Assert.True(lastPage.HasPreviousPage);
        Assert.False(lastPage.HasNextPage);

        List<string> coveredNames = firstPage.Items
            .Concat(lastPage.Items)
            .Select(profile => profile.Name)
            .OrderBy(name => name)
            .ToList();
        List<string> expectedNames = new List<string>
        {
            "paginated-union-alpha",
            "paginated-union-bravo",
            "paginated-union-charlie"
        };
        Assert.Equal(expectedNames, coveredNames);
    }

    [Fact]
    public async Task PaginatedAsync_PageBeyondRange_ReturnsEmptyItemsWithMetadata()
    {
        await SeedProfilesAsync("paginated-beyond");
        await using Context context = _fixture.CreateContext();
        RepositoryBaseHarness harness = CreateHarness(context);
        Expression<Func<Profile, bool>> filter = profile =>
            profile.Name == "paginated-beyond-alpha"
            || profile.Name == "paginated-beyond-bravo"
            || profile.Name == "paginated-beyond-charlie";

        PaginatedResult<Profile> page = await harness.Paginated(9, 2, filter);

        Assert.Empty(page.Items);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(2, page.TotalPages);
        Assert.True(page.HasPreviousPage);
        Assert.False(page.HasNextPage);
    }

    [Fact]
    public async Task AddAsync_ThenSaveChanges_PersistsEntity()
    {
        string name = $"harness-add-{Guid.NewGuid():N}";

        await using Context context = _fixture.CreateContext();
        RepositoryBaseHarness harness = CreateHarness(context);

        Profile profile = new Profile { Name = name, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        await harness.AddEntity(profile);
        await harness.SaveChanges();

        await using Context verifyContext = _fixture.CreateContext();
        Profile stored = await verifyContext.Profiles.SingleAsync(candidate => candidate.Name == name);

        Assert.Equal(name, stored.Name);
    }

    [Fact]
    public async Task AddRangeAsync_ThenSaveChanges_PersistsEveryEntity()
    {
        string prefix = $"harness-range-{Guid.NewGuid():N}";

        await using Context context = _fixture.CreateContext();
        RepositoryBaseHarness harness = CreateHarness(context);

        Profile first = new Profile { Name = prefix + "-one", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        Profile second = new Profile { Name = prefix + "-two", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        await harness.AddEntities(new[] { first, second });
        await harness.SaveChanges();

        await using Context verifyContext = _fixture.CreateContext();
        List<string> names = await verifyContext.Profiles
            .Where(candidate => candidate.Name == prefix + "-one" || candidate.Name == prefix + "-two")
            .Select(candidate => candidate.Name)
            .ToListAsync();

        Assert.Equal(2, names.Count);
    }

    [Fact]
    public async Task Remove_ThenSaveChanges_DeletesEntity()
    {
        await SeedProfilesAsync("harness-remove");

        await using Context context = _fixture.CreateContext();
        RepositoryBaseHarness harness = CreateHarness(context);
        Profile tracked = await context.Profiles.SingleAsync(candidate => candidate.Name == "harness-remove-alpha");

        harness.RemoveEntity(tracked);
        await harness.SaveChanges();

        await using Context verifyContext = _fixture.CreateContext();
        bool stillThere = await verifyContext.Profiles.AnyAsync(candidate => candidate.Name == "harness-remove-alpha");

        Assert.False(stillThere);
    }

    [Fact]
    public async Task RemoveRange_ThenSaveChanges_DeletesEveryEntity()
    {
        await SeedProfilesAsync("harness-remove-range");

        await using Context context = _fixture.CreateContext();
        RepositoryBaseHarness harness = CreateHarness(context);
        List<Profile> tracked = await context.Profiles
            .Where(candidate => candidate.Name == "harness-remove-range-bravo" || candidate.Name == "harness-remove-range-charlie")
            .ToListAsync();

        harness.RemoveEntities(tracked);
        await harness.SaveChanges();

        await using Context verifyContext = _fixture.CreateContext();
        bool anyLeft = await verifyContext.Profiles.AnyAsync(candidate =>
            candidate.Name == "harness-remove-range-bravo" || candidate.Name == "harness-remove-range-charlie");

        Assert.False(anyLeft);
    }

    [Fact]
    public async Task SaveChanges_PersistsPendingContextChanges()
    {
        string name = $"harness-save-{Guid.NewGuid():N}";

        await using Context context = _fixture.CreateContext();
        RepositoryBaseHarness harness = CreateHarness(context);

        context.Profiles.Add(new Profile { Name = name, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await harness.SaveChanges();

        await using Context verifyContext = _fixture.CreateContext();
        bool persisted = await verifyContext.Profiles.AnyAsync(candidate => candidate.Name == name);

        Assert.True(persisted);
    }

    [Fact]
    public void PaginatedResult_ComputedProperties_MatchPaginationMath()
    {
        PaginatedResult<Profile> populated = new PaginatedResult<Profile>(new List<Profile>(), 3, 1, 2);
        PaginatedResult<Profile> empty = new PaginatedResult<Profile>(new List<Profile>(), 0, 3, 2);

        Assert.Equal(2, populated.TotalPages);
        Assert.False(populated.HasPreviousPage);
        Assert.True(populated.HasNextPage);
        Assert.Equal(0, empty.TotalPages);
        Assert.True(empty.HasPreviousPage);
        Assert.False(empty.HasNextPage);
    }

    private RepositoryBaseHarness CreateHarness(Context context)
    {
        return new RepositoryBaseHarness(context, NullLogger.Instance);
    }

    private async Task<List<int>> SeedProfilesAsync(string prefix)
    {
        await using Context context = _fixture.CreateContext();
        List<Profile> profiles = new List<Profile>
        {
            new Profile { Name = prefix + "-alpha", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Profile { Name = prefix + "-bravo", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Profile { Name = prefix + "-charlie", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
        };
        context.Profiles.AddRange(profiles);
        await context.SaveChangesAsync();
        return profiles.Select(profile => profile.Id).ToList();
    }
}
