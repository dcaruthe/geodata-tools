using FluentAssertions;
using GeoTools.Shared.Application.Options;
using GeoTools.Shared.Infrastructure.Geodata;
using GeoTools.Shared.Infrastructure.Processes;
using GeoTools.Shared.Infrastructure.State;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GeoTools.Shared.Infrastructure.Tests;

public sealed class BorderExtractorTests
{
    [Fact]
    public async Task Extract_runs_the_pipeline_filters_the_levels_and_caches()
    {
        await using var database = await TestDatabase.CreateAsync();
        var root = Directory.CreateTempSubdirectory("geotools-extract-");
        try
        {
            var request = CreateRequest(root.FullName);
            File.WriteAllText(request.SourcePath, "pbf");
            var runner = new FakeRunner();
            var progress = new Collector<double>();
            var extractor = CreateExtractor(database, runner);

            var result = await extractor.ExtractAsync(request, progress);

            result.FeatureCount.Should().Be(2);
            result.SizeBytes.Should().BeGreaterThan(0);
            File.Exists(request.BordersPath).Should().BeTrue();
            File.ReadAllText(request.LevelsPath).Should().Be("2,4");

            var lines = File.ReadAllText(request.BordersPath)
                .Split('\u001e', StringSplitOptions.RemoveEmptyEntries);
            lines.Should().HaveCount(2);

            var cache = new GeometryCacheStore(database.Factory);
            var key = GeoTools.Shared.Infrastructure.Geospatial.BorderCacheKeys.Compute(
                request.BordersPath,
                0.002,
                [2, 4]
            );
            (await cache.TryGetAsync(key)).Should().NotBeNull();

            runner.Specs.Should().HaveCount(3);
            runner
                .Specs[0]
                .Arguments.Should()
                .ContainInOrder(
                    "tags-filter",
                    "-O",
                    "-o",
                    request.SourcePath,
                    "r/admin_level=2",
                    "r/admin_level=4",
                    "--progress"
                );
            runner.Specs[1].Arguments.Should().Contain("r/boundary=administrative");
            runner
                .Specs[2]
                .Arguments.Should()
                .ContainInOrder(
                    "export",
                    "-f",
                    "geojsonseq",
                    "--geometry-types=polygon,linestring",
                    "-a",
                    "id"
                );

            progress.Values.Should().NotBeEmpty();
            progress.Values[^1].Should().Be(1);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task A_failed_stage_removes_the_output_and_reports_the_stage()
    {
        await using var database = await TestDatabase.CreateAsync();
        var root = Directory.CreateTempSubdirectory("geotools-extract-fail-");
        try
        {
            var request = CreateRequest(root.FullName);
            File.WriteAllText(request.SourcePath, "pbf");
            var extractor = CreateExtractor(
                database,
                new FakeRunner(spec =>
                    spec.Arguments[0] == "export"
                        ? new ProcessResult(1, "", "boom")
                        : new ProcessResult(0, "", "")
                )
            );

            var act = async () => await extractor.ExtractAsync(request, null);

            await act.Should()
                .ThrowAsync<InvalidOperationException>()
                .WithMessage("*export failed*boom*");
            File.Exists(request.BordersPath).Should().BeFalse();
            File.Exists(request.LevelsPath).Should().BeFalse();
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Cancellation_propagates_and_removes_the_output()
    {
        await using var database = await TestDatabase.CreateAsync();
        var root = Directory.CreateTempSubdirectory("geotools-extract-cancel-");
        try
        {
            var request = CreateRequest(root.FullName);
            File.WriteAllText(request.SourcePath, "pbf");
            var extractor = CreateExtractor(
                database,
                new FakeRunner(_ => throw new OperationCanceledException())
            );

            var act = async () => await extractor.ExtractAsync(request, null);

            await act.Should().ThrowAsync<OperationCanceledException>();
            File.Exists(request.BordersPath).Should().BeFalse();
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static ExtractionRequest CreateRequest(string root)
    {
        var borders = Directory.CreateDirectory(Path.Combine(root, "borders")).FullName;
        return new ExtractionRequest(
            "africa",
            Path.Combine(root, "africa-latest.osm.pbf"),
            Path.Combine(borders, "africa-boundaries.geojsonseq"),
            Path.Combine(borders, "africa-boundaries.levels"),
            Path.Combine(root, ".work"),
            [2, 4],
            0.002
        );
    }

    private static BorderExtractor CreateExtractor(TestDatabase database, FakeRunner runner) =>
        new(
            runner,
            new GeometryCacheStore(database.Factory),
            Options.Create(new GeodataOptions()),
            TimeProvider.System,
            NullLogger<BorderExtractor>.Instance
        );

    private sealed class FakeRunner(Func<ProcessSpec, ProcessResult>? respond = null)
        : IProcessRunner
    {
        private static readonly string[] Features =
        [
            Feature(
                "2",
                """{"type":"Polygon","coordinates":[[[10,10],[20,10],[20,20],[10,10]]]}"""
            ),
            Feature("4", """{"type":"LineString","coordinates":[[0,0],[1,1]]}"""),
            Feature("6", """{"type":"LineString","coordinates":[[0,0],[1,1]]}"""),
            "{\"type\":\"Feature\",\"properties\":",
        ];

        public List<ProcessSpec> Specs { get; } = [];

        public Task<ProcessResult> RunAsync(
            ProcessSpec spec,
            Action<string>? onStandardError = null,
            CancellationToken cancellationToken = default
        )
        {
            Specs.Add(spec);
            onStandardError?.Invoke("Progress: 50%");
            var result = respond?.Invoke(spec) ?? new ProcessResult(0, "", "");
            if (result.ExitCode == 0 && spec.Arguments[0] == "export")
            {
                var index = spec.Arguments.ToList().IndexOf("-o");
                File.WriteAllText(
                    spec.Arguments[index + 1],
                    string.Concat(Features.Select(text => "\u001e" + text + "\n"))
                );
            }

            return Task.FromResult(result);
        }

        private static string Feature(string level, string geometry) =>
            $$"""{"type":"Feature","properties":{"admin_level":{{level}}},"geometry":{{geometry}}}""";
    }

    private sealed class Collector<T> : IProgress<T>
    {
        public List<T> Values { get; } = [];

        public void Report(T value) => Values.Add(value);
    }
}
