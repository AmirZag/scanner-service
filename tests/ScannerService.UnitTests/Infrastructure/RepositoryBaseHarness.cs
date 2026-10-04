using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ScannerService.Domain.Entities;
using ScannerService.Infrastructure.Persistence;
using ScannerService.Infrastructure.Repositories;

namespace ScannerService.UnitTests.Infrastructure;

/// <summary>
/// Test-only derived class that re-exposes every protected <see cref="RepositoryBase{T}"/>
/// member as public surface. RepositoryBase is slated for deletion in a later refactor;
/// until then this harness keeps the coverage gate green for every helper, including the
/// dead ones (ExistsAsync, CountAsync, ListAsync, PaginatedAsync, EntityExistsAsync, both
/// GetByIdAsync overloads, AddAsync(T), AddRangeAsync, Remove, RemoveRange, SaveChangesAsync).
/// </summary>
public sealed class RepositoryBaseHarness : RepositoryBase<Profile>
{
    public RepositoryBaseHarness(Context context, ILogger logger)
        : base(context, logger)
    {
    }

    public Task<bool> EntityExists(int id)
    {
        return base.EntityExistsAsync(id);
    }

    public Task<Profile?> GetById(int id)
    {
        return base.GetByIdAsync(id);
    }

    public Task<Profile?> GetByIdWithToken(int id, CancellationToken cancellationToken)
    {
        return base.GetByIdAsync(id, cancellationToken);
    }

    public Task<bool> Exists(Expression<Func<Profile, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return base.ExistsAsync(predicate, cancellationToken);
    }

    public Task<int> Count(Expression<Func<Profile, bool>>? predicate = null, CancellationToken cancellationToken = default)
    {
        return base.CountAsync(predicate, cancellationToken);
    }

    public Task<List<Profile>> List(Expression<Func<Profile, bool>>? predicate = null, CancellationToken cancellationToken = default)
    {
        return base.ListAsync(predicate, cancellationToken);
    }

    public Task<PaginatedResult<Profile>> Paginated(
        int page,
        int pageSize,
        Expression<Func<Profile, bool>>? predicate = null,
        CancellationToken cancellationToken = default)
    {
        return base.PaginatedAsync(page, pageSize, predicate, cancellationToken);
    }

    public Task SaveChanges(CancellationToken cancellationToken = default)
    {
        return base.SaveChangesAsync(cancellationToken);
    }

    public Task AddEntity(Profile entity, CancellationToken cancellationToken = default)
    {
        return base.AddAsync(entity, cancellationToken);
    }

    public Task AddEntities(IEnumerable<Profile> entities, CancellationToken cancellationToken = default)
    {
        return base.AddRangeAsync(entities, cancellationToken);
    }

    public void RemoveEntity(Profile entity)
    {
        base.Remove(entity);
    }

    public void RemoveEntities(IEnumerable<Profile> entities)
    {
        base.RemoveRange(entities);
    }
}
