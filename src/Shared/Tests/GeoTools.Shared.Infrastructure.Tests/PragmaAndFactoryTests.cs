using FluentAssertions;
using GeoTools.Shared.Application.Options;
using GeoTools.Shared.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GeoTools.Shared.Infrastructure.Tests;

public sealed class PragmaAndFactoryTests
{
    [Fact]
    public async Task Synchronous_queries_open_a_configured_connection()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = await database.Factory.CreateDbContextAsync();

        context.Messages.Count().Should().Be(0);
    }

    [Fact]
    public async Task The_registered_factory_creates_the_database_directory_and_schema()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"geotools-factory-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "nested", "state.db");
        try
        {
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(
                        new Dictionary<string, string?> { ["Storage:DatabasePath"] = path }
                    )
                    .Build()
            );
            services.SetupInfrastructureLayer();

            using var provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<IDbContextFactory<GeoToolsDbContext>>();
            await using var context = await factory.CreateDbContextAsync();
            await SqliteSchema.InitializeAsync(context, CancellationToken.None);

            File.Exists(path).Should().BeTrue();
            context.Messages.Count().Should().Be(0);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
