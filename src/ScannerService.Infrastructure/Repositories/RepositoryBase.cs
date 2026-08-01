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
    /// Saves changes to the database.
    /// </summary>
    protected async Task SaveChangesAsync()
    {
        await Context.SaveChangesAsync();
    }

    /// <summary>
    /// Adds a new entity to the database.
    /// </summary>
    protected async Task AddAsync(T entity)
    {
        await Context.Set<T>().AddAsync(entity);
    }

    /// <summary>
    /// Removes an entity from the database.
    /// </summary>
    protected void Remove(T entity)
    {
        Context.Set<T>().Remove(entity);
    }
}
