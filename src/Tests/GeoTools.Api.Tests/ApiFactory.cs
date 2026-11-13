using GeoTools.Shared.Domain.Geospatial;
using GeoTools.Shared.Infrastructure.Geospatial;
using GeoTools.Shared.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GeoTools.Api.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private const string RunId = "b5f5e4c3d2a142009988776655443322";

    private const string BorderFixture = """
        {"type":"Feature","properties":{"admin_level":2},"geometry":{"type":"Polygon","coordinates":[[[0,0],[10,0],[10,10],[0,10],[0,0]]]}}
        {"type":"Feature","properties":{"admin_level":4},"geometry":{"type":"Polygon","coordinates":[[[2,2],[4,2],[4,4],[2,4],[2,2]]]}}
        """;

    public ApiFactory()
    {
        Root = Path.Combine(Path.GetTempPath(), $"geotools-api-{Guid.NewGuid():N}");
        DatabasePath = Path.Combine(Root, "state.db");
        GeodataRoot = Path.Combine(Root, "geodata");
        var borders = Path.Combine(GeodataRoot, "borders");
        Directory.CreateDirectory(borders);
        File.WriteAllText(Path.Combine(borders, "africa.geojsonseq"), BorderFixture);
        CreateDatabase();
    }

    public string Root { get; }

    public string DatabasePath { get; }

    public string GeodataRoot { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Storage:DatabasePath", DatabasePath);
        builder.UseSetting("Storage:ReadOnly", "true");
        builder.UseSetting("Geodata:RootPath", GeodataRoot);
        builder.UseSetting("Geodata:RegionList", "africa");
        builder.UseSetting("Geodata:ExtractAdminLevelsList", "2,4");
        builder.UseSetting("Link:SocketPath", Path.Combine(Root, "launcher.sock"));
        builder.UseSetting("Link:KeyPath", Path.Combine(Root, "launcher.key"));
        builder.UseSetting("Logging:LogLevel:Default", "Warning");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
        catch (IOException) { }
    }

    private void CreateDatabase()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<GeoToolsDbContext>(options =>
            options.UseSqlite($"Data Source={DatabasePath};Pooling=False")
        );

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<GeoToolsDbContext>>();
        using var context = factory.CreateDbContext();
        SqliteSchema.InitializeAsync(context, CancellationToken.None).GetAwaiter().GetResult();

        context.Artifacts.Add(
            new ArtifactRow
            {
                Key = "borders:africa",
                Kind = "Borders",
                Region = "africa",
                Path = Path.Combine(GeodataRoot, "borders", "africa.geojsonseq"),
                SizeBytes = 2048,
                ModifiedAt = 0,
                Fingerprint = "fixture",
                AdminLevels = "2,4",
                Bounds = null,
                Status = "Current",
                UpdatedAt = 0,
            }
        );
        context.Runs.Add(
            new RunRow
            {
                Id = RunId,
                Reason = "startup",
                Status = "Succeeded",
                StartedAt = 1_000,
                FinishedAt = 2_000,
                Error = null,
            }
        );
        context.Steps.Add(
            new StepRow
            {
                RunId = RunId,
                StepKey = "pull:africa",
                Ordinal = 0,
                Stage = "pull",
                Label = "Downloading africa",
                Status = "Succeeded",
                Attempts = 1,
                Fraction = 1,
            }
        );
        context.Regions.Add(Region("region:de", "Deutschland", 2, 0, 0, 10, 10));
        context.Regions.Add(Region("region:de-bayern", "Bayern", 4, 2, 2, 4, 4));
        context.BorderLines.Add(Line("region:line:0", 4, 0, 0, 1, 1));
        context.SaveChanges();
    }

    private static RegionRow Region(
        string id,
        string name,
        int level,
        double minLon,
        double minLat,
        double maxLon,
        double maxLat
    )
    {
        var ring = new GeoPolyline(
            [
                new GeoPoint(minLon, minLat),
                new GeoPoint(maxLon, minLat),
                new GeoPoint(maxLon, maxLat),
                new GeoPoint(minLon, maxLat),
                new GeoPoint(minLon, minLat),
            ],
            Closed: true,
            AdminLevel: level
        );
        var bounds = new GeoBounds(minLon, minLat, maxLon, maxLat);
        return new RegionRow
        {
            Id = id,
            SourceKey = "fixture",
            Region = "africa",
            OsmId = id["region:".Length..],
            Name = name,
            BorderType = "nation",
            AdminLevel = level,
            ParentId = null,
            ParentResolved = false,
            MinLongitude = minLon,
            MinLatitude = minLat,
            MaxLongitude = maxLon,
            MaxLatitude = maxLat,
            Area = (maxLon - minLon) * (maxLat - minLat),
            PointCount = 5,
            Blob = GeometryBlobCodec.Encode([new GeometryFeature(null, [ring])], bounds),
            CreatedAt = 0,
        };
    }

    private static BorderLineRow Line(
        string id,
        int level,
        double minLon,
        double minLat,
        double maxLon,
        double maxLat
    )
    {
        var polyline = new GeoPolyline(
            [new GeoPoint(minLon, minLat), new GeoPoint(maxLon, maxLat)],
            Closed: false,
            AdminLevel: level
        );
        var bounds = new GeoBounds(minLon, minLat, maxLon, maxLat);
        return new BorderLineRow
        {
            Id = id,
            SourceKey = "fixture",
            Region = "africa",
            AdminLevel = level,
            MinLongitude = minLon,
            MinLatitude = minLat,
            MaxLongitude = maxLon,
            MaxLatitude = maxLat,
            PointCount = 2,
            Blob = GeometryBlobCodec.Encode([new GeometryFeature(null, [polyline])], bounds),
            CreatedAt = 0,
        };
    }
}
