using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScannerService.Infrastructure.Persistence;

namespace ScannerService.Infrastructure.Repositories;

/// <summary>
/// Base repository class providing common functionality for all repositories.
/// Eliminates code duplication and ensures consistent behavior.
/// </summary>
/// <typeparam name="T">The entity type</typeparam>
public abstract class RepositoryBase<T> where T : class
{
    protected readonly Context Context;
    protected readonly ILogger Logger;

    protected RepositoryBase(Context context, ILogger logger)
    {
        Context = context;
        Logger = logger;
    }

    /// <summary>
    /// Checks if an entity with the given ID exists.
    /// </summary>
    protected async Task<bool> EntityExistsAsync(int id)
    {
        return await Context.Set<T>().FindAsync(id) != null;
    }

    /// <summary>
    /// Gets an entity by ID or returns null.
    /// </summary>
    protected async Task<T?> GetByIdAsync(int id)
    {
        return await Context.Set<T>().FindAsync(id);
    }

    /// <summary>
    /// Gets an entity by ID with cancellation token support.
    /// </summary>
    protected async Task<T?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        return await Context.Set<T>().FindAsync([id], cancellationToken);
    }

    /// <summary>
    /// Checks if any entity matches the specified predicate.
    /// </summary>
    protected async Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return await Context.Set<T>().AnyAsync(predicate, cancellationToken);
    }

    /// <summary>
    /// Gets the count of entities matching the specified predicate.
    /// </summary>
    protected async Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default)
    {
        return predicate == null
            ? await Context.Set<T>().CountAsync(cancellationToken)
            : await Context.Set<T>().CountAsync(predicate, cancellationToken);
    }

    /// <summary>
    /// Gets a list of all entities or those matching the specified predicate.
    /// </summary>
    protected async Task<List<T>> ListAsync(
        Expression<Func<T, bool>>? predicate = null,
        CancellationToken cancellationToken = default)
    {
        var query = Context.Set<T>().AsQueryable();

        if (predicate != null)
        {
            query = query.Where(predicate);
        }

        return await query.ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Gets paginated results.
    /// </summary>
    protected async Task<PaginatedResult<T>> PaginatedAsync(
        int page,
        int pageSize,
        Expression<Func<T, bool>>? predicate = null,
        CancellationToken cancellationToken = default)
    {
        var query = Context.Set<T>().AsQueryable();

        if (predicate != null)
        {
            query = query.Where(predicate);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PaginatedResult<T>(items, totalCount, page, pageSize);
    }

    /// <summary>
    /// Saves changes to the database.
    /// </summary>
    protected async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await Context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Adds a new entity to the database.
    /// </summary>
    protected async Task AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        await Context.Set<T>().AddAsync(entity, cancellationToken);
    }

    /// <summary>
    /// Adds multiple entities to the database.
    /// </summary>
    protected async Task AddRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
    {
        await Context.Set<T>().AddRangeAsync(entities, cancellationToken);
    }

    /// <summary>
    /// Removes an entity from the database.
    /// </summary>
    protected void Remove(T entity)
    {
        Context.Set<T>().Remove(entity);
    }

    /// <summary>
    /// Removes multiple entities from the database.
    /// </summary>
    protected void RemoveRange(IEnumerable<T> entities)
    {
        Context.Set<T>().RemoveRange(entities);
    }
}

/// <summary>
/// Represents a paginated result set.
/// </summary>
/// <typeparam name="T">The type of items in the result.</typeparam>
public sealed record PaginatedResult<T>(
    List<T> Items,
    int TotalCount,
    int Page,
    int PageSize)
{
    /// <summary>
    /// Gets the total number of pages.
    /// </summary>
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);

    /// <summary>
    /// Gets whether there is a previous page.
    /// </summary>
    public bool HasPreviousPage => Page > 1;

    /// <summary>
    /// Gets whether there is a next page.
    /// </summary>
    public bool HasNextPage => Page < TotalPages;
}
