using FluentAssertions;
using GeoTools.Shared.Application.Geospatial;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Domain.Geospatial;
using GeoTools.Shared.Infrastructure.Geodata;
using GeoTools.Shared.Infrastructure.Geospatial;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeoTools.Shared.Infrastructure.Tests;

public sealed class RegionIndexBuilderTests
{
    [Fact]
    public async Task WriteAsync_maps_polygons_with_ids_to_regions_and_the_rest_to_lines()
    {
        var writer = new CapturingWriter();
        var builder = new RegionIndexBuilder(
            new EmptyIndex(),
            writer,
            NullLogger<RegionIndexBuilder>.Instance
        );
        var square = Square();
        var features = new List<ParsedFeature>
        {
            new(2, "42", "Alpha", "nation", [square], [new GeoRing(square.Points)]),
            new(4, null, null, null, [Line()], []),
            new(2, null, null, null, [square], [new GeoRing(square.Points)]),
        };

        var rows = await builder.WriteAsync("africa", "key", features, CancellationToken.None);

        rows.Should().Be(3);
        writer.Region.Should().Be("africa");
        writer.SourceKey.Should().Be("key");
        writer.Regions.Should().HaveCount(1);
        writer.Regions![0].Id.Should().Be("region:42");
        writer.Regions[0].OsmId.Should().Be("42");
        writer.Regions[0].Name.Should().Be("Alpha");
        writer.Regions[0].BorderType.Should().Be("nation");
        writer.Lines.Should().HaveCount(2);
        writer.Lines![0].Id.Should().Be("region:line:africa:0");
        writer.Lines![1].Id.Should().Be("region:line:africa:1");
    }

    [Fact]
    public async Task WriteAsync_keeps_the_first_duplicate_region_id()
    {
        var writer = new CapturingWriter();
        var builder = new RegionIndexBuilder(
            new EmptyIndex(),
            writer,
            NullLogger<RegionIndexBuilder>.Instance
        );
        var square = Square();
        var features = new List<ParsedFeature>
        {
            new(2, "1", "First", null, [square], [new GeoRing(square.Points)]),
            new(2, "1", "Second", null, [square], [new GeoRing(square.Points)]),
        };

        var rows = await builder.WriteAsync("africa", "key", features, CancellationToken.None);

        rows.Should().Be(1);
        writer.Regions.Should().HaveCount(1);
        writer.Regions![0].Name.Should().Be("First");
    }

    [Fact]
    public async Task WriteAsync_computes_bounds_and_sums_the_area_of_every_ring()
    {
        var writer = new CapturingWriter();
        var builder = new RegionIndexBuilder(
            new EmptyIndex(),
            writer,
            NullLogger<RegionIndexBuilder>.Instance
        );
        var square = Square();
        var features = new List<ParsedFeature>
        {
            new(
                2,
                "1",
                null,
                null,
                [square, square],
                [new GeoRing(square.Points), new GeoRing(square.Points)]
            ),
        };

        await builder.WriteAsync("africa", "key", features, CancellationToken.None);

        var region = writer.Regions!.Single();
        region.Bounds.Should().Be(new GeoBounds(0, 0, 1, 1));
        region.PointCount.Should().Be(10);
        region.Area.Should().BeApproximately(2 * RingArea(square.Points), 1e-12);
    }

    [Fact]
    public async Task WriteAsync_returns_zero_and_writes_nothing_for_no_features()
    {
        var writer = new CapturingWriter();
        var builder = new RegionIndexBuilder(
            new EmptyIndex(),
            writer,
            NullLogger<RegionIndexBuilder>.Instance
        );

        var rows = await builder.WriteAsync("africa", "key", [], CancellationToken.None);

        rows.Should().Be(0);
        writer.Regions.Should().BeEmpty();
        writer.Lines.Should().BeEmpty();
    }

    [Fact]
    public async Task IsIndexedAsync_and_DeleteExceptAsync_delegate_to_the_underlying_index()
    {
        var index = new TrackingIndex();
        var writer = new CapturingWriter();
        var builder = new RegionIndexBuilder(
            index,
            writer,
            NullLogger<RegionIndexBuilder>.Instance
        );

        await builder.IsIndexedAsync("key", CancellationToken.None);
        index.Queried.Should().Equal("key");

        await builder.DeleteExceptAsync(["a", "b"], CancellationToken.None);
        writer.DeletedSources.Should().Equal("a", "b");
    }

    private static GeoPolyline Square() =>
        new(
            [
                new GeoPoint(0, 0),
                new GeoPoint(1, 0),
                new GeoPoint(1, 1),
                new GeoPoint(0, 1),
                new GeoPoint(0, 0),
            ],
            Closed: true,
            AdminLevel: 2
        );

    private static GeoPolyline Line() =>
        new([new GeoPoint(0, 0), new GeoPoint(1, 1)], Closed: false, AdminLevel: 4);

    private static double RingArea(IReadOnlyList<GeoPoint> ring)
    {
        double sum = 0;
        for (var i = 0; i < ring.Count - 1; i++)
        {
            var first = MapProjection.Project(ring[i]);
            var second = MapProjection.Project(ring[i + 1]);
            sum += first.X * second.Y - second.X * first.Y;
        }

        return Math.Abs(sum) / 2;
    }

    private sealed class CapturingWriter : IRegionIndexWriter
    {
        public string? Region { get; private set; }
        public string? SourceKey { get; private set; }
        public IReadOnlyList<RegionWrite> Regions { get; private set; } = [];
        public IReadOnlyList<BorderLineWrite> Lines { get; private set; } = [];
        public IReadOnlyCollection<string> DeletedSources { get; private set; } = [];

        public Task WriteSourceAsync(
            string region,
            string sourceKey,
            IReadOnlyList<RegionWrite> regions,
            IReadOnlyList<BorderLineWrite> lines,
            CancellationToken cancellationToken = default
        )
        {
            Region = region;
            SourceKey = sourceKey;
            Regions = regions;
            Lines = lines;
            return Task.CompletedTask;
        }

        public Task DeleteExceptSourcesAsync(
            IReadOnlyCollection<string> sourceKeys,
            CancellationToken cancellationToken = default
        )
        {
            DeletedSources = sourceKeys;
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<RegionSummary>> ResolveChildrenAsync(
            string parentId,
            int childLevel,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<RegionSummary>>([]);
    }

    private sealed class EmptyIndex : IRegionIndex
    {
        public Task<RegionDetail?> GetRegionAsync(
            string id,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<RegionDetail?>(null);

        public Task<IReadOnlyList<RegionDetail>> GetRegionsAsync(
            IReadOnlyList<string> ids,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<RegionDetail>>([]);

        public Task<IReadOnlyList<RegionSummary>> ListRegionsAsync(
            int[]? levels,
            GeoBounds? bounds,
            string? parentId,
            string? search,
            int limit,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<RegionSummary>>([]);

        public Task<IReadOnlyList<BorderLine>> ListLinesAsync(
            int[] levels,
            GeoBounds? bounds,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<BorderLine>>([]);

        public Task<bool> IsSourceIndexedAsync(
            string sourceKey,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(false);

        public Task<RegionIndexStats> GetStatsAsync(
            CancellationToken cancellationToken = default
        ) => Task.FromResult(new RegionIndexStats(0, 0, [], GeoBounds.World, 0));

        public Task<IReadOnlyList<RegionSummary>> FindContainingAsync(
            GeoPoint point,
            IReadOnlyList<int> levels,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<RegionSummary>>([]);

        public Task<IReadOnlyList<RegionSummary>> GetChildrenAsync(
            string parentId,
            int childLevel,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<RegionSummary>>([]);

        public Task<IReadOnlyList<RegionSummary>> FindChildrenAsync(
            string parentId,
            int childLevel,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<RegionSummary>>([]);

        public Task<bool> IsResolvedAsync(
            string parentId,
            int childLevel,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(false);
    }

    private sealed class TrackingIndex : IRegionIndex
    {
        public List<string> Queried { get; } = [];

        public Task<RegionDetail?> GetRegionAsync(
            string id,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<RegionDetail?>(null);

        public Task<IReadOnlyList<RegionDetail>> GetRegionsAsync(
            IReadOnlyList<string> ids,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<RegionDetail>>([]);

        public Task<IReadOnlyList<RegionSummary>> ListRegionsAsync(
            int[]? levels,
            GeoBounds? bounds,
            string? parentId,
            string? search,
            int limit,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<RegionSummary>>([]);

        public Task<IReadOnlyList<BorderLine>> ListLinesAsync(
            int[] levels,
            GeoBounds? bounds,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<BorderLine>>([]);

        public Task<bool> IsSourceIndexedAsync(
            string sourceKey,
            CancellationToken cancellationToken = default
        )
        {
            Queried.Add(sourceKey);
            return Task.FromResult(true);
        }

        public Task<RegionIndexStats> GetStatsAsync(
            CancellationToken cancellationToken = default
        ) => Task.FromResult(new RegionIndexStats(0, 0, [], GeoBounds.World, 0));

        public Task<IReadOnlyList<RegionSummary>> FindContainingAsync(
            GeoPoint point,
            IReadOnlyList<int> levels,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<RegionSummary>>([]);

        public Task<IReadOnlyList<RegionSummary>> GetChildrenAsync(
            string parentId,
            int childLevel,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<RegionSummary>>([]);

        public Task<IReadOnlyList<RegionSummary>> FindChildrenAsync(
            string parentId,
            int childLevel,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<RegionSummary>>([]);

        public Task<bool> IsResolvedAsync(
            string parentId,
            int childLevel,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(false);
    }
}
