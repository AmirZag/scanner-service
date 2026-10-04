using System;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ScannerService.Infrastructure.Persistence;
using Xunit;

namespace ScannerService.UnitTests.Infrastructure;

/// <summary>
/// Owns one named shared-cache SQLite in-memory database for a single test class.
/// The connection is opened once and stays open for the fixture lifetime, because a named
/// in-memory SQLite database exists only while at least one connection to it remains open.
/// Each fixture instance uses a unique database name, so test classes never share state and
/// no database files are written into the test bin folder.
/// </summary>
public sealed class InfrastructureSqliteFixture : IAsyncLifetime, IDisposable
{
    private readonly SqliteConnection _connection =
        new SqliteConnection($"Data Source=memdb_{Guid.NewGuid():N};Mode=Memory;Cache=Shared");

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();

        await using Context context = CreateContext();
        await context.Database.EnsureCreatedAsync();
    }

    /// <summary>
    /// Creates a fresh, change-tracker-free Context over the shared in-memory database.
    /// Reads through a new Context prove persistence instead of identity-map hits.
    /// </summary>
    public Context CreateContext()
    {
        DbContextOptionsBuilder<Context> optionsBuilder = new DbContextOptionsBuilder<Context>();
        optionsBuilder.UseSqlite(_connection);
        return new Context(optionsBuilder.Options);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }
}
