/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Listenarr.Infrastructure.DependencyInjection;
using Listenarr.Infrastructure.Persistence.Repositories;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

/// <summary>
/// Background polling and per-request session writes used to contend for SQLite's single
/// writer (and, without WAL, block every reader), stalling page loads and failing session
/// checks with "database is locked".
/// </summary>
[Trait("Name", "SqliteWriteContentionTests")]
[Trait("Category", "Infrastructure")]
public sealed class SqliteWriteContentionTests : BaseTests
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "listenarr-contention-" + Guid.NewGuid().ToString("N"));
    private readonly SaveCounter _saves = new();
    private readonly ServiceProvider _sqliteProvider;

    public SqliteWriteContentionTests()
    {
        Directory.CreateDirectory(_dir);
        var services = new ServiceCollection();
        services.AddDbContextFactory<ListenArrDbContext>(options => options
            .UseSqlite($"Data Source={Path.Combine(_dir, "listenarr.db")};Pooling=False", sqlite =>
                sqlite.MigrationsAssembly(typeof(ListenArrDbContext).Assembly.GetName().Name))
            .AddInterceptors(_saves));
        _sqliteProvider = services.BuildServiceProvider();
        _sqliteProvider.ApplyListenarrDatabaseMigrations();
    }

    private IDbContextFactory<ListenArrDbContext> Factory =>
        _sqliteProvider.GetRequiredService<IDbContextFactory<ListenArrDbContext>>();

    [Fact]
    public void Startup_PutsTheDatabaseInWalMode()
    {
        using var ctx = Factory.CreateDbContext();
        Assert.Equal("wal", SqlitePragmaInitializer.ReadJournalMode(ctx));
    }

    [Fact]
    public async Task UpdateAsync_SkipsTheWriteWhenAPolledDownloadHasNotChanged()
    {
        var repository = new EfDownloadRepository(Factory, NullLogger<EfDownloadRepository>.Instance);
        await repository.AddAsync(new DownloadBuilder().WithId("seeding").WithTitle("Seveneves")
            .WithStatus(DownloadStatus.Moved).WithProgress(100).Build());

        var polled = (await repository.GetByIdAsync("seeding"))!;
        _saves.Count = 0;
        await repository.UpdateAsync(polled);
        Assert.Equal(0, _saves.Count);

        polled.SetMetadata("CanBeRemoved", true);
        await repository.UpdateAsync(polled);
        Assert.Equal(1, _saves.Count);
        Assert.Equal("True", (await repository.GetByIdAsync("seeding"))!.GetMetadataString("CanBeRemoved"));
    }

    [Fact]
    public async Task GetByTokenHash_WritesLastAccessedAtMostEveryFewMinutes()
    {
        await using (var seed = Factory.CreateDbContext())
        {
            seed.UserSessions.AddRange(
                Session("fresh", lastAccessedAgo: TimeSpan.Zero),
                Session("stale", lastAccessedAgo: EfUserSessionRepository.LastAccessedResolution + TimeSpan.FromMinutes(1)));
            await seed.SaveChangesAsync();
        }

        await using var ctx = Factory.CreateDbContext();
        var sessions = new EfUserSessionRepository(ctx);
        _saves.Count = 0;
        Assert.NotNull(await sessions.GetByTokenHashAsync("fresh"));
        Assert.NotNull(await sessions.GetByTokenHashAsync("fresh"));
        Assert.Equal(0, _saves.Count);

        var stale = await sessions.GetByTokenHashAsync("stale");
        Assert.Equal(1, _saves.Count);
        Assert.True(DateTime.UtcNow - stale!.LastAccessed < TimeSpan.FromMinutes(1));
    }

    private static UserSession Session(string tokenHash, TimeSpan lastAccessedAgo) => new()
    {
        Username = "admin",
        TokenHash = tokenHash,
        ExpiresAt = DateTime.UtcNow.AddDays(1),
        LastAccessed = DateTime.UtcNow - lastAccessedAgo,
    };

    public override async Task DisposeAsync()
    {
        await _sqliteProvider.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        await base.DisposeAsync();
    }

    private sealed class SaveCounter : SaveChangesInterceptor
    {
        public int Count;

        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Count);
            return base.SavedChangesAsync(eventData, result, cancellationToken);
        }

        public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
        {
            Interlocked.Increment(ref Count);
            return base.SavedChanges(eventData, result);
        }
    }
}
