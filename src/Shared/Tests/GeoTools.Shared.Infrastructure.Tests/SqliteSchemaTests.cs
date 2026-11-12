using FluentAssertions;
using GeoTools.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GeoTools.Shared.Infrastructure.Tests;

public sealed class SqliteSchemaTests
{
    [Fact]
    public async Task Initialize_creates_the_tables_and_sets_the_schema_version()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = await database.Factory.CreateDbContextAsync();

        var tables = await context
            .Database.SqlQueryRaw<string>(
                "SELECT name AS Value FROM sqlite_master WHERE type = 'table' ORDER BY name"
            )
            .ToListAsync();

        tables
            .Should()
            .Contain([
                "Artifacts",
                "FeatureFlags",
                "GeometryCache",
                "HandledRequests",
                "Messages",
                "Runs",
                "Settings",
                "Steps",
                "Subscriptions",
            ]);

        var version = await context
            .Database.SqlQueryRaw<int>("SELECT user_version AS Value FROM pragma_user_version")
            .SingleAsync();
        version.Should().Be(SqliteSchema.CurrentVersion);
    }

    [Fact]
    public async Task Initialize_is_idempotent()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = await database.Factory.CreateDbContextAsync();

        var act = async () => await SqliteSchema.InitializeAsync(context, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Initialize_rejects_a_newer_schema()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = await database.Factory.CreateDbContextAsync();
        await context.Database.ExecuteSqlRawAsync("PRAGMA user_version = 99;");

        var act = async () => await SqliteSchema.InitializeAsync(context, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*newer*");
    }

    [Fact]
    public async Task Connections_use_wal_full_sync_and_foreign_keys()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = await database.Factory.CreateDbContextAsync();

        (
            await context
                .Database.SqlQueryRaw<string>(
                    "SELECT journal_mode AS Value FROM pragma_journal_mode"
                )
                .SingleAsync()
        )
            .Should()
            .Be("wal");
        (
            await context
                .Database.SqlQueryRaw<int>("SELECT synchronous AS Value FROM pragma_synchronous")
                .SingleAsync()
        )
            .Should()
            .Be(2);
        (
            await context
                .Database.SqlQueryRaw<int>("SELECT foreign_keys AS Value FROM pragma_foreign_keys")
                .SingleAsync()
        )
            .Should()
            .Be(1);
    }

    [Fact]
    public async Task Steps_require_an_existing_run()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = await database.Factory.CreateDbContextAsync();
        context.Steps.Add(
            new StepRow
            {
                RunId = Guid.NewGuid().ToString("N"),
                StepKey = "load",
                Stage = "load",
                Label = "Load",
                Status = "Pending",
            }
        );

        var act = async () => await context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
