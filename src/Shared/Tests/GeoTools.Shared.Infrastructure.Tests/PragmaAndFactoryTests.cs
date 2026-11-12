using FluentAssertions;
using GeoTools.Shared.Application.Geospatial;
using GeoTools.Shared.Application.Messaging;
using GeoTools.Shared.Application.Messaging.Messages;
using GeoTools.Shared.Application.Options;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Infrastructure.Messaging;
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
    public async Task A_read_only_factory_refuses_writes()
    {
        await using var database = await TestDatabase.CreateAsync();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Storage:DatabasePath"] = database.Path,
                        ["Storage:ReadOnly"] = "true",
                    }
                )
                .Build()
        );
        services.SetupInfrastructureDataLayer();

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<GeoToolsDbContext>>();
        await using var context = await factory.CreateDbContextAsync();
        context.Settings.Add(
            new SettingRow
            {
                Key = "x",
                Value = "1",
                Source = "test",
                UpdatedAt = 0,
            }
        );

        var act = async () => await context.SaveChangesAsync();

        var exception = await act.Should().ThrowAsync<DbUpdateException>();
        exception
            .WithInnerException<SqliteException>()
            .Where(inner => inner.Message.Contains("readonly", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_data_layer_registers_a_read_only_bus_and_no_workers()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder().AddInMemoryCollection().Build()
        );
        services.SetupInfrastructureDataLayer();

        await using var provider = services.BuildServiceProvider();

        provider.GetService<IMessageBus>().Should().BeOfType<NullMessageBus>();
        provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>().Should().BeEmpty();
        provider.GetService<IStateInitializer>().Should().BeNull();
        provider.GetService<IBorderGeometryLoader>().Should().NotBeNull();

        var publish = async () =>
            await provider
                .GetRequiredService<IMessageBus>()
                .PublishAsync(new SettingsChanged("x", "y", DateTimeOffset.UnixEpoch));

        await publish.Should().ThrowAsync<NotSupportedException>();
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
