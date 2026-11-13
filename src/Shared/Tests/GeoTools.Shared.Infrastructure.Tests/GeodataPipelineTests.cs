using FluentAssertions;
using GeoTools.Shared.Application.Geodata;
using GeoTools.Shared.Application.Geospatial;
using GeoTools.Shared.Application.Messaging;
using GeoTools.Shared.Application.Messaging.Messages;
using GeoTools.Shared.Application.Options;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Domain.Geospatial;
using GeoTools.Shared.Infrastructure.Geodata;
using GeoTools.Shared.Infrastructure.Messaging;
using GeoTools.Shared.Infrastructure.State;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GeoTools.Shared.Infrastructure.Tests;

public sealed class GeodataPipelineTests
{
    [Fact]
    public async Task A_current_dataset_loads_without_downloading_or_extracting()
    {
        await using var fixture = await PipelineFixture.CreateAsync();
        fixture.WriteCurrentData();

        var (outcome, finished) = await fixture.RunAsync(BootstrapRequest.Startup);

        outcome.Status.Should().Be(RunStatus.Succeeded);
        finished.Status.Should().Be(RunStatus.Succeeded);
        fixture.Downloader.Requests.Should().BeEmpty();
        fixture.Extractor.Requests.Should().BeEmpty();
        fixture.Available.Should().NotBeEmpty();
        fixture.Geometry.Geometry.Polylines.Should().NotBeEmpty();
    }

    [Fact]
    public async Task A_missing_border_file_is_extracted_and_published()
    {
        await using var fixture = await PipelineFixture.CreateAsync();
        fixture.WriteSource();

        var (outcome, finished) = await fixture.RunAsync(BootstrapRequest.Startup);

        outcome.Status.Should().Be(RunStatus.Succeeded);
        finished.Status.Should().Be(RunStatus.Succeeded);
        fixture.Extractor.Requests.Should().ContainSingle();
        fixture.Downloader.Requests.Should().BeEmpty();
        File.Exists(fixture.BordersPath).Should().BeTrue();
        (await fixture.Artifacts.GetAsync(ArtifactKeys.Borders("africa"))).Should().NotBeNull();
        fixture.Available.Should().NotBeEmpty();
        fixture
            .Steps.Should()
            .Contain(step =>
                step.Key.Value == "extract:africa" && step.Status == StepStatus.Succeeded
            );
    }

    [Fact]
    public async Task A_missing_source_is_downloaded_then_extracted()
    {
        await using var fixture = await PipelineFixture.CreateAsync();

        var (outcome, _) = await fixture.RunAsync(BootstrapRequest.Startup);

        outcome.Status.Should().Be(RunStatus.Succeeded);
        fixture.Downloader.Requests.Should().ContainSingle();
        fixture.Extractor.Requests.Should().ContainSingle();
        (await fixture.Artifacts.GetAsync(ArtifactKeys.Pbf("africa"))).Should().NotBeNull();
    }

    [Fact]
    public async Task A_missing_source_with_auto_pull_disabled_fails_clearly()
    {
        await using var fixture = await PipelineFixture.CreateAsync();
        fixture.Settings.SetFlag(FeatureFlag.GeodataAutoPull, false);

        var (outcome, finished) = await fixture.RunAsync(BootstrapRequest.Startup);

        outcome.Status.Should().Be(RunStatus.Failed);
        finished.Status.Should().Be(RunStatus.Failed);
        outcome.Error.Should().Contain("automatic download is disabled");
        fixture.Downloader.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Cancellation_finishes_the_run_as_cancelled()
    {
        await using var fixture = await PipelineFixture.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        fixture.Downloader.Respond = (_, _) =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException();
        };

        var (outcome, finished) = await fixture.RunAsync(
            BootstrapRequest.Startup,
            cancellation.Token
        );

        outcome.Status.Should().Be(RunStatus.Cancelled);
        finished.Status.Should().Be(RunStatus.Cancelled);
    }

    [Fact]
    public async Task An_unchanged_pull_skips_the_extraction()
    {
        await using var fixture = await PipelineFixture.CreateAsync();
        fixture.WriteCurrentData();
        fixture.Downloader.ReturnUnchanged = true;

        var (outcome, _) = await fixture.RunAsync(new BootstrapRequest("test", RebuildScope.Pull));

        outcome.Status.Should().Be(RunStatus.Succeeded);
        fixture.Extractor.Requests.Should().BeEmpty();
        fixture
            .Steps.Should()
            .Contain(step =>
                step.Key.Value == "extract:africa" && step.Status == StepStatus.Skipped
            );
    }

    [Fact]
    public async Task An_unchanged_download_without_an_artifact_is_recorded()
    {
        await using var fixture = await PipelineFixture.CreateAsync();
        fixture.Downloader.ReturnUnchanged = true;

        var (outcome, _) = await fixture.RunAsync(BootstrapRequest.Startup);

        outcome.Status.Should().Be(RunStatus.Succeeded);
        (await fixture.Artifacts.GetAsync(ArtifactKeys.Pbf("africa"))).Should().NotBeNull();
        fixture.Extractor.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task The_cache_scope_clears_the_geometry_cache()
    {
        await using var fixture = await PipelineFixture.CreateAsync();
        fixture.WriteCurrentData();
        await fixture.Cache.PutAsync(
            new GeometryCacheEntry(
                "stale",
                "africa",
                [2, 4],
                0.002,
                1,
                1,
                GeoBounds.World,
                [1],
                DateTimeOffset.UnixEpoch
            )
        );

        var (outcome, _) = await fixture.RunAsync(
            new BootstrapRequest("test", RebuildScope.GeometryCache)
        );

        outcome.Status.Should().Be(RunStatus.Succeeded);
        (await fixture.Cache.TryGetAsync("stale")).Should().BeNull();
    }

    private sealed class PipelineFixture : IAsyncDisposable
    {
        private PipelineFixture(
            TestDatabase database,
            string root,
            GeodataOptions options,
            TestSettings settings,
            SqliteMessageBus bus,
            ArtifactStore artifacts,
            GeometryCacheStore cache,
            BorderGeometryStore geometry,
            FakeLoader loader,
            FakeDownloader downloader,
            FakeExtractor extractor
        )
        {
            Database = database;
            Root = root;
            Options = options;
            Settings = settings;
            Bus = bus;
            Artifacts = artifacts;
            Cache = cache;
            Geometry = geometry;
            Loader = loader;
            Downloader = downloader;
            Extractor = extractor;
            Pipeline = new GeodataPipeline(
                Microsoft.Extensions.Options.Options.Create(options),
                settings,
                artifacts,
                cache,
                loader,
                geometry,
                downloader,
                extractor,
                bus,
                TimeProvider.System,
                NullLogger<GeodataPipeline>.Instance
            );
        }

        public TestDatabase Database { get; }
        public string Root { get; }
        public GeodataOptions Options { get; }
        public TestSettings Settings { get; }
        public SqliteMessageBus Bus { get; }
        public ArtifactStore Artifacts { get; }
        public GeometryCacheStore Cache { get; }
        public BorderGeometryStore Geometry { get; }
        public FakeLoader Loader { get; }
        public FakeDownloader Downloader { get; }
        public FakeExtractor Extractor { get; }
        public GeodataPipeline Pipeline { get; }
        public List<BootstrapStepChanged> Steps { get; } = [];
        public List<BordersAvailable> Available { get; } = [];
        public TaskCompletionSource<BootstrapRunFinished> Finished { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string SourcePath => Path.Combine(Root, "africa-latest.osm.pbf");

        public string BordersPath => Path.Combine(Root, "borders", "africa-boundaries.geojsonseq");

        public string LevelsPath => Path.Combine(Root, "borders", "africa-boundaries.levels");

        public static async Task<PipelineFixture> CreateAsync()
        {
            var root = Directory.CreateTempSubdirectory("geotools-pipeline-").FullName;
            var database = await TestDatabase.CreateAsync();
            var options = new GeodataOptions
            {
                RootPath = root,
                BordersSubdirectory = "borders",
                Regions = ["africa"],
                ExtractAdminLevels = [2, 4],
                SimplifyToleranceDegrees = 0.002,
                SourceBaseUrl = "http://example.test/",
                RetryCount = 0,
                InactivityTimeoutSeconds = 5,
                RunOnStartup = false,
            };
            var settings = new TestSettings();
            var bus = new SqliteMessageBus(
                database.Factory,
                TimeProvider.System,
                NullLogger<SqliteMessageBus>.Instance
            );
            var artifacts = new ArtifactStore(database.Factory);
            var cache = new GeometryCacheStore(database.Factory);
            var geometry = new BorderGeometryStore();
            var loader = new FakeLoader(Path.Combine(root, "borders"));
            var downloader = new FakeDownloader();
            var extractor = new FakeExtractor();

            var fixture = new PipelineFixture(
                database,
                root,
                options,
                settings,
                bus,
                artifacts,
                cache,
                geometry,
                loader,
                downloader,
                extractor
            );

            await bus.SubscribeAsync(
                "pipeline-test",
                [
                    MessageTopics.BootstrapStepChanged,
                    MessageTopics.BootstrapRunFinished,
                    MessageTopics.BordersAvailable,
                ],
                fixture.HandleAsync,
                startAt: null
            );
            await bus.StartAsync();
            return fixture;
        }

        public async Task<(BootstrapOutcome Outcome, BootstrapRunFinished Finished)> RunAsync(
            BootstrapRequest request,
            CancellationToken cancellationToken = default
        )
        {
            var outcome = await Pipeline.RunAsync(request, cancellationToken);
            var finished = await Finished.Task.WaitAsync(TimeSpan.FromSeconds(10));
            return (outcome, finished);
        }

        public void WriteSource()
        {
            Directory.CreateDirectory(Root);
            File.WriteAllText(SourcePath, "pbf");
            File.SetLastWriteTimeUtc(SourcePath, DateTime.UtcNow.AddHours(-2));
        }

        public void WriteCurrentData()
        {
            WriteSource();
            Directory.CreateDirectory(Path.GetDirectoryName(BordersPath)!);
            File.WriteAllText(BordersPath, "\u001e{}");
            File.WriteAllText(LevelsPath, "2,4");
        }

        public async ValueTask DisposeAsync()
        {
            await Bus.StopAsync();
            await Bus.DisposeAsync();
            await Database.DisposeAsync();
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }

        private Task HandleAsync(MessageEnvelope envelope, CancellationToken cancellationToken)
        {
            switch (envelope.Topic)
            {
                case MessageTopics.BootstrapStepChanged:
                    lock (Steps)
                        Steps.Add(MessageSerialization.Deserialize<BootstrapStepChanged>(envelope));
                    break;
                case MessageTopics.BordersAvailable:
                    lock (Available)
                        Available.Add(MessageSerialization.Deserialize<BordersAvailable>(envelope));
                    break;
                case MessageTopics.BootstrapRunFinished:
                    Finished.TrySetResult(
                        MessageSerialization.Deserialize<BootstrapRunFinished>(envelope)
                    );
                    break;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakeLoader(string bordersDirectory) : IBorderGeometryLoader
    {
        public Task<BorderLoadResult> LoadAsync(
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default
        )
        {
            var ready =
                Directory.Exists(bordersDirectory)
                && Directory.GetFiles(bordersDirectory, "*.geojsonseq").Length > 0;

            if (!ready)
                return Task.FromResult(
                    new BorderLoadResult(BorderGeometry.Empty, string.Empty, [], 0, 0)
                );

            var geometry = BorderGeometry.FromGeo(
                [
                    new GeoPolyline(
                        [
                            new GeoPoint(-10, -10),
                            new GeoPoint(10, -10),
                            new GeoPoint(10, 10),
                            new GeoPoint(-10, -10),
                        ],
                        Closed: true,
                        AdminLevel: 2
                    ),
                ],
                new GeoBounds(-10, -10, 10, 10)
            );

            progress?.Report(1);
            return Task.FromResult(new BorderLoadResult(geometry, "fingerprint", [2], 0, 1));
        }
    }

    private sealed class FakeDownloader : IPbfDownloader
    {
        public List<DownloadRequest> Requests { get; } = [];

        public bool ReturnUnchanged { get; set; }

        public Func<DownloadRequest, CancellationToken, DownloadResult>? Respond { get; set; }

        public Task<DownloadResult> DownloadAsync(
            DownloadRequest request,
            IProgress<DownloadProgress>? progress,
            CancellationToken cancellationToken = default
        )
        {
            Requests.Add(request);
            if (Respond is not null)
                return Task.FromResult(Respond(request, cancellationToken));

            Directory.CreateDirectory(Path.GetDirectoryName(request.DestinationPath)!);
            File.WriteAllText(request.DestinationPath, "pbf");
            progress?.Report(new DownloadProgress(3, 3, 1, 0));
            return Task.FromResult(
                new DownloadResult(!ReturnUnchanged, 3, DateTimeOffset.UnixEpoch, "etag")
            );
        }
    }

    private sealed class FakeExtractor : IBorderExtractor
    {
        public List<ExtractionRequest> Requests { get; } = [];

        public Task<ExtractionResult> ExtractAsync(
            ExtractionRequest request,
            IProgress<double>? progress,
            CancellationToken cancellationToken = default
        )
        {
            Requests.Add(request);
            Directory.CreateDirectory(Path.GetDirectoryName(request.BordersPath)!);
            File.WriteAllText(request.BordersPath, "\u001e{}");
            File.WriteAllText(request.LevelsPath, "2,4");
            progress?.Report(1);
            return Task.FromResult(
                new ExtractionResult(2, DateTimeOffset.UnixEpoch, 1, GeoBounds.World)
            );
        }
    }
}
