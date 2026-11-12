using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using GeoTools.Shared.Application.Options;
using GeoTools.Shared.Infrastructure.Geodata;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GeoTools.Shared.Infrastructure.Tests;

public sealed class HttpPbfDownloaderTests
{
    [Fact]
    public async Task A_200_response_writes_the_file_and_reports_progress()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent("hello"u8.ToArray()),
            Headers = { ETag = new EntityTagHeaderValue("\"v1\"") },
        });
        var directory = Directory.CreateTempSubdirectory("geotools-download-");
        try
        {
            var destination = Path.Combine(directory.FullName, "africa.osm.pbf");
            var progress = new Collector<DownloadProgress>();
            var downloader = Create(handler);

            var result = await downloader.DownloadAsync(
                new DownloadRequest(
                    "africa",
                    new Uri("http://example.test/africa.osm.pbf"),
                    destination,
                    Force: false,
                    Validator: null
                ),
                progress
            );

            File.ReadAllText(destination).Should().Be("hello");
            File.Exists(destination + ".part").Should().BeFalse();
            result.Changed.Should().BeTrue();
            result.SizeBytes.Should().Be(5);
            result.Validator.Should().Be("\"v1\"");
            progress.Values.Should().NotBeEmpty();
            progress.Values[^1].BytesReceived.Should().Be(5);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task A_304_response_keeps_the_existing_file()
    {
        var directory = Directory.CreateTempSubdirectory("geotools-304-");
        try
        {
            var destination = Path.Combine(directory.FullName, "africa.osm.pbf");
            File.WriteAllText(destination, "old");
            var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotModified));
            var downloader = Create(handler);

            var result = await downloader.DownloadAsync(
                new DownloadRequest(
                    "africa",
                    new Uri("http://example.test/africa.osm.pbf"),
                    destination,
                    Force: false,
                    Validator: "\"v1\""
                ),
                null
            );

            File.ReadAllText(destination).Should().Be("old");
            result.Changed.Should().BeFalse();
            result.SizeBytes.Should().Be(3);
            handler
                .Requests[0]
                .Headers.TryGetValues("If-None-Match", out var values)
                .Should()
                .BeTrue();
            values.Should().Contain("\"v1\"");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task A_206_response_appends_to_the_partial_file()
    {
        var directory = Directory.CreateTempSubdirectory("geotools-206-");
        try
        {
            var destination = Path.Combine(directory.FullName, "africa.osm.pbf");
            File.WriteAllText(destination + ".part", "abc");
            var handler = new StubHandler(request =>
            {
                request.Headers.Range!.Ranges.Single().From.Should().Be(3);
                var content = new ByteArrayContent("def"u8.ToArray());
                content.Headers.ContentRange = new ContentRangeHeaderValue(3, 5, 6);
                return new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content };
            });
            var downloader = Create(handler);

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

            File.ReadAllText(destination).Should().Be("abcdef");
            result.SizeBytes.Should().Be(6);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task A_200_response_after_a_range_request_restarts_the_file()
    {
        var directory = Directory.CreateTempSubdirectory("geotools-restart-");
        try
        {
            var destination = Path.Combine(directory.FullName, "africa.osm.pbf");
            File.WriteAllText(destination + ".part", "abc");
            var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent("xyz"u8.ToArray()),
            });
            var downloader = Create(handler);

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

            File.ReadAllText(destination).Should().Be("xyz");
            result.SizeBytes.Should().Be(3);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task A_transient_failure_is_retried()
    {
        var directory = Directory.CreateTempSubdirectory("geotools-retry-");
        try
        {
            var destination = Path.Combine(directory.FullName, "africa.osm.pbf");
            var calls = 0;
            var handler = new StubHandler(_ =>
                Interlocked.Increment(ref calls) == 1
                    ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    : new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent("ok"u8.ToArray()),
                    }
            );
            var downloader = Create(
                handler,
                new GeodataOptions { RetryCount = 1, InactivityTimeoutSeconds = 5 }
            );

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

            result.Changed.Should().BeTrue();
            calls.Should().Be(2);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Cancellation_is_not_retried()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var handler = new StubHandler(request =>
            throw new OperationCanceledException(cancellation.Token)
        );
        var directory = Directory.CreateTempSubdirectory("geotools-cancel-");
        try
        {
            var downloader = Create(handler);

            var act = async () =>
                await downloader.DownloadAsync(
                    new DownloadRequest(
                        "africa",
                        new Uri("http://example.test/africa.osm.pbf"),
                        Path.Combine(directory.FullName, "africa.osm.pbf"),
                        Force: false,
                        Validator: null
                    ),
                    null,
                    cancellation.Token
                );

            await act.Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task A_rejected_range_fails_with_an_actionable_message()
    {
        var directory = Directory.CreateTempSubdirectory("geotools-416-");
        try
        {
            var destination = Path.Combine(directory.FullName, "africa.osm.pbf");
            File.WriteAllText(destination + ".part", "abc");
            var downloader = Create(
                new StubHandler(_ => new HttpResponseMessage(
                    HttpStatusCode.RequestedRangeNotSatisfiable
                ))
            );

            var act = async () =>
                await downloader.DownloadAsync(
                    new DownloadRequest(
                        "africa",
                        new Uri("http://example.test/africa.osm.pbf"),
                        destination,
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
    public async Task Exhausting_the_retries_reports_the_last_error()
    {
        var directory = Directory.CreateTempSubdirectory("geotools-exhausted-");
        try
        {
            var destination = Path.Combine(directory.FullName, "africa.osm.pbf");
            var calls = 0;
            var downloader = Create(
                new StubHandler(_ =>
                {
                    Interlocked.Increment(ref calls);
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                }),
                new GeodataOptions { RetryCount = 1, InactivityTimeoutSeconds = 5 }
            );

            var act = async () =>
                await downloader.DownloadAsync(
                    new DownloadRequest(
                        "africa",
                        new Uri("http://example.test/africa.osm.pbf"),
                        destination,
                        Force: false,
                        Validator: null
                    ),
                    null
                );

            await act.Should().ThrowAsync<IOException>();
            calls.Should().Be(2);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void The_disk_space_guard_rejects_an_impossible_request()
    {
        var act = () => DiskSpaceGuard.EnsureAvailable(Path.GetTempPath(), long.MaxValue);
        var allowed = () => DiskSpaceGuard.EnsureAvailable(Path.GetTempPath(), 1);

        act.Should().Throw<IOException>().WithMessage("*free disk space*");
        allowed.Should().NotThrow();
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

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request);
            return Task.FromResult(_respond(request));
        }
    }

    private sealed class Collector<T> : IProgress<T>
    {
        public List<T> Values { get; } = [];

        public void Report(T value) => Values.Add(value);
    }
}
