using GeoTools.Shared.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GeoTools.Shared.Infrastructure.Tests;

internal sealed class TestDatabase : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    private TestDatabase(ServiceProvider provider, string directory, string path)
    {
        _provider = provider;
        Directory = directory;
        Path = path;
        Factory = provider.GetRequiredService<IDbContextFactory<GeoToolsDbContext>>();
    }

    public string Directory { get; }

    public string Path { get; }

    public IDbContextFactory<GeoToolsDbContext> Factory { get; }

    public static async Task<TestDatabase> CreateAsync()
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"geotools-db-{Guid.NewGuid():N}"
        );
        System.IO.Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, "state.db");

        var services = new ServiceCollection();
        services.AddDbContextFactory<GeoToolsDbContext>(
            (_, options) =>
                options
                    .UseSqlite($"Data Source={path};Pooling=False;Foreign Keys=True")
                    .AddInterceptors(new SqlitePragmaInterceptor())
        );

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<GeoToolsDbContext>>();
        await using var context = await factory.CreateDbContextAsync();
        await SqliteSchema.InitializeAsync(context, CancellationToken.None);

        return new TestDatabase(provider, directory, path);
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        SqliteConnection.ClearAllPools();
        if (System.IO.Directory.Exists(Directory))
            System.IO.Directory.Delete(Directory, recursive: true);
    }
}
