using System.Net;
using FluentAssertions;
using GeoTools.Shared.Application.Geodata;
using GeoTools.Shared.Application.Messaging;
using GeoTools.Shared.Application.Messaging.Messages;
using GeoTools.Shared.Application.Options;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Infrastructure.Geodata;
using GeoTools.Shared.Infrastructure.Processes;
using GeoTools.Shared.Infrastructure.State;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GeoTools.Shared.Infrastructure.Tests;

public sealed class WorkerFailureTests
{
    [Fact]
    public async Task A_pipeline_failure_is_logged_and_the_worker_keeps_running()
    {
        var worker = new GeodataWorker(
            new ThrowingPipeline(),
            new FakeJobStore(),
            Options.Create(new GeodataOptions { RunOnStartup = false }),
            NullLogger<GeodataWorker>.Instance
        );
        var bus = new FakeMessageBus();
        await worker.AttachAsync(bus);
        await worker.StartAsync(CancellationToken.None);

        await bus.DeliverAsync(
            MessageTopics.RebuildRequested,
            new RebuildRequested("r", RebuildScope.All, false, null, DateTimeOffset.UnixEpoch)
        );
        await Task.Delay(150);

        var stop = async () => await worker.StopAsync(CancellationToken.None);
        await stop.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Stopping_cancels_an_inflight_run_without_escaping()
    {
        var pipeline = new BlockingPipeline();
        var worker = new GeodataWorker(
            pipeline,
            new FakeJobStore(),
            Options.Create(new GeodataOptions { RunOnStartup = false }),
            NullLogger<GeodataWorker>.Instance
        );
        var bus = new FakeMessageBus();
        await worker.AttachAsync(bus);
        await worker.StartAsync(CancellationToken.None);
        await bus.DeliverAsync(
            MessageTopics.RebuildRequested,
            new RebuildRequested("r", RebuildScope.All, false, null, DateTimeOffset.UnixEpoch)
        );
        await pipeline.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var stop = async () => await worker.StopAsync(CancellationToken.None);

        await stop.Should().NotThrowAsync();
    }

    private sealed class ThrowingPipeline : IGeodataPipeline
    {
        public Task<BootstrapOutcome> RunAsync(
            BootstrapRequest request,
            CancellationToken cancellationToken = default
        ) => throw new InvalidOperationException("pipeline failed");
    }

    private sealed class BlockingPipeline : IGeodataPipeline
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<BootstrapOutcome> RunAsync(
            BootstrapRequest request,
            CancellationToken cancellationToken = default
        )
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new BootstrapOutcome(RunId.New(), RunStatus.Succeeded, null);
        }
    }
}

public sealed class SettingsFailureTests
{
    [Fact]
    public async Task Save_failures_are_returned_as_rejections()
    {
        var database = await TestDatabase.CreateAsync();
        var store = new SettingsStore(
            database.Factory,
            Options.Create(new MapOptions { AdminLevels = [2] }),
            Options.Create(new GeodataOptions()),
            Options.Create(new FeaturesOptions()),
            new FakeMessageBus(),
            TimeProvider.System,
            NullLogger<SettingsStore>.Instance
        );
        await store.ReloadAsync();
        await database.DisposeAsync();

        var display = await store.SetDisplayAdminLevelsAsync([4]);
        var flag = await store.SetFlagAsync(FeatureFlag.MapGrid, false);
        var reset = await store.ResetToConfigurationAsync();

        display.Accepted.Should().BeFalse();
        flag.Accepted.Should().BeFalse();
        reset.Accepted.Should().BeFalse();
    }
}

public sealed class ExtractorCacheFailureTests
{
    [Fact]
    public async Task A_cache_failure_does_not_fail_the_extraction()
    {
        var root = Directory.CreateTempSubdirectory("geotools-cache-failure-");
        try
        {
            var borders = Directory
                .CreateDirectory(Path.Combine(root.FullName, "borders"))
                .FullName;
            var source = Path.Combine(root.FullName, "africa-latest.osm.pbf");
            File.WriteAllText(source, "pbf");
            var request = new ExtractionRequest(
                "africa",
                source,
                Path.Combine(borders, "africa-boundaries.geojsonseq"),
                Path.Combine(borders, "africa-boundaries.levels"),
                Path.Combine(root.FullName, ".work"),
                [2],
                0.002
            );
            var extractor = new BorderExtractor(
                new ExportingRunner(),
                new ThrowingCache(),
                Options.Create(new GeodataOptions()),
                TimeProvider.System,
                NullLogger<BorderExtractor>.Instance
            );

            var result = await extractor.ExtractAsync(request, null);

            result.FeatureCount.Should().Be(1);
            File.Exists(request.BordersPath).Should().BeTrue();
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private sealed class ExportingRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(
            ProcessSpec spec,
            Action<string>? onStandardError = null,
            CancellationToken cancellationToken = default
        )
        {
            if (spec.Arguments[0] == "export")
            {
                var index = spec.Arguments.ToList().IndexOf("-o");
                File.WriteAllText(
                    spec.Arguments[index + 1],
                    "\u001e{\"type\":\"Feature\",\"properties\":{\"admin_level\":2},\"geometry\":{\"type\":\"LineString\",\"coordinates\":[[0,0],[1,1]]}}\n"
                );
            }

            return Task.FromResult(new ProcessResult(0, "", ""));
        }
    }

    private sealed class ThrowingCache : IGeometryCache
    {
        public Task<GeometryCacheEntry?> TryGetAsync(
            string key,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<GeometryCacheEntry?>(null);

        public Task PutAsync(
            GeometryCacheEntry entry,
            CancellationToken cancellationToken = default
        ) => throw new InvalidOperationException("cache failed");

        public Task DeleteExceptAsync(
            IReadOnlyCollection<string> keys,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;

        public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

public sealed class DownloadFailureTests
{
    [Fact]
    public async Task A_stalled_stream_times_out_and_is_reported()
    {
        var directory = Directory.CreateTempSubdirectory("geotools-stall-");
        try
        {
            var downloader = Create(
                new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new SlowContent("hello"u8.ToArray()),
                }),
                new GeodataOptions { RetryCount = 0, InactivityTimeoutSeconds = 1 }
            );

            var act = async () =>
                await downloader.DownloadAsync(
                    new DownloadRequest(
                        "africa",
                        new Uri("http://example.test/africa.osm.pbf"),
                        Path.Combine(directory.FullName, "africa.osm.pbf"),
                        Force: false,
                        Validator: null
                    ),
                    null
                );

            await act.Should().ThrowAsync<IOException>().WithMessage("*Could not download*");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task A_response_without_a_length_still_writes_the_file()
    {
        var directory = Directory.CreateTempSubdirectory("geotools-nolength-");
        try
        {
            var body = "streamed"u8.ToArray();
            var downloader = Create(
                new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new AnonymousContent(body),
                })
            );
            var destination = Path.Combine(directory.FullName, "africa.osm.pbf");

            var result = await downloader.DownloadAsync(
                new DownloadRequest(
                    "africa",
                    new Uri("http://example.test/africa.osm.pbf"),
                    destination,
                    Force: false,
                    Validator: null
                ),
                null
            );

            File.ReadAllBytes(destination).Should().Equal(body);
            result.SizeBytes.Should().Be(body.Length);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static HttpPbfDownloader Create(StubHandler handler, GeodataOptions? options = null) =>
        new(
            new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
            Options.Create(
                options ?? new GeodataOptions { RetryCount = 0, InactivityTimeoutSeconds = 5 }
            ),
            TimeProvider.System,
            NullLogger<HttpPbfDownloader>.Instance
        );

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
            _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(_respond(request));
    }

    private sealed class SlowContent(byte[] body) : HttpContent
    {
        protected override Task SerializeToStreamAsync(
            Stream stream,
            System.Net.TransportContext? context
        ) => SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(
            Stream stream,
            System.Net.TransportContext? context,
            CancellationToken cancellationToken
        )
        {
            await stream.WriteAsync(body.AsMemory(), cancellationToken);
            await stream.FlushAsync(cancellationToken);
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = body.Length;
            return true;
        }
    }

    private sealed class AnonymousContent(byte[] body) : HttpContent
    {
        protected override Task SerializeToStreamAsync(
            Stream stream,
            System.Net.TransportContext? context
        ) => stream.WriteAsync(body.AsMemory()).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
