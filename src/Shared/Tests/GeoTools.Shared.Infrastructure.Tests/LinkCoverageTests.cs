using FluentAssertions;
using GeoTools.Shared.Application.Link;
using GeoTools.Shared.Application.Messaging;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Domain.Geospatial;
using GeoTools.Shared.Infrastructure.Geospatial;
using GeoTools.Shared.Infrastructure.Link;
using GeoTools.Shared.Infrastructure.Logging;
using GeoTools.Shared.Infrastructure.Messaging;
using Microsoft.Extensions.Logging;

namespace GeoTools.Shared.Infrastructure.Tests;

public sealed class LinkCoverageTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"geotools-link-cov-{Guid.NewGuid():N}"
    );

    public LinkCoverageTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void A_key_that_is_not_hex_is_rejected()
    {
        var path = Path.Combine(_directory, "bad.key");
        File.WriteAllText(path, "not-hex");

        var act = () => LinkKey.Load(path);

        act.Should().Throw<InvalidDataException>();
        LinkKey.TryLoad(path).Should().BeNull();
    }

    [Fact]
    public void A_key_with_the_wrong_length_is_rejected()
    {
        var path = Path.Combine(_directory, "short.key");
        File.WriteAllText(path, "00ff");

        var act = () => LinkKey.Load(path);

        act.Should().Throw<InvalidDataException>().WithMessage("*Expected 32*");
    }

    [Fact]
    public void A_key_destination_that_cannot_be_written_is_reported()
    {
        var path = Path.Combine(_directory, "blocked");
        Directory.CreateDirectory(path);

        var act = () => LinkKey.EnsureCreated(path);

        act.Should().Throw<IOException>();
    }

    [Fact]
    public async Task A_frame_longer_than_the_cap_is_rejected()
    {
        var header = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(
            header,
            LinkProtocol.MaxFrameBytes + 1
        );
        var reader = new LinkFrameReader(
            new MemoryStream(header),
            new LinkKeyRing(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
        );

        var act = async () => await reader.ReadAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task The_read_only_cache_never_writes()
    {
        var inner = new RecordingCache();
        var cache = new ReadOnlyGeometryCache(inner);

        await cache.PutAsync(inner.Entry);
        await cache.DeleteExceptAsync(["k"]);
        await cache.ClearAsync();
        var hit = await cache.TryGetAsync("k");

        hit.Should().BeSameAs(inner.Entry);
        inner.Writes.Should().Be(0);
    }

    [Fact]
    public async Task The_null_bus_refuses_to_publish()
    {
        var bus = new NullMessageBus();

        await bus.StartAsync();
        await bus.StopAsync();
        await bus.SubscribeAsync("x", ["topic"], (_, _) => Task.CompletedTask);
        await bus.ResetSubscriptionAsync("x");

        var act = async () => await bus.PublishAsync(new object());

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public void The_log_store_uses_the_last_entries_and_ignores_an_unknown_level()
    {
        var store = new InMemoryLogStore();
        var logger = store.CreateLogger("test");
        logger.LogTrace("trace");
        logger.LogInformation("info");

        store.Read("NotALevel", 10, null).Entries.Should().HaveCount(2);
        logger.IsEnabled(LogLevel.None).Should().BeFalse();
        logger.BeginScope("scope").Should().BeNull();
        logger.Log(LogLevel.None, 0, "ignored", null, (_, _) => "ignored");
        store.Read(null, 10, null).Entries.Should().HaveCount(2);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
        catch (IOException) { }
    }

    private sealed class RecordingCache : IGeometryCache
    {
        public GeometryCacheEntry Entry { get; } =
            new(
                "k",
                "africa",
                [2],
                0.002,
                1,
                1,
                GeoBounds.World,
                [1, 2, 3],
                DateTimeOffset.UnixEpoch
            );

        public int Writes { get; private set; }

        public Task<GeometryCacheEntry?> TryGetAsync(
            string key,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<GeometryCacheEntry?>(Entry);

        public Task PutAsync(
            GeometryCacheEntry entry,
            CancellationToken cancellationToken = default
        )
        {
            Writes++;
            return Task.CompletedTask;
        }

        public Task DeleteExceptAsync(
            IReadOnlyCollection<string> keys,
            CancellationToken cancellationToken = default
        )
        {
            Writes++;
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            Writes++;
            return Task.CompletedTask;
        }
    }
}
