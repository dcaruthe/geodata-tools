using System.Buffers.Binary;
using System.Security.Cryptography;
using FluentAssertions;
using GeoTools.Shared.Application.Link;
using GeoTools.Shared.Infrastructure.Link;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GeoTools.Shared.Infrastructure.Tests;

public sealed class LinkTests : IAsyncDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"geotools-link-{Guid.NewGuid():N}"
    );

    private string SocketPath => Path.Combine(_directory, "launcher.sock");

    private string KeyPath => Path.Combine(_directory, "launcher.key");

    public LinkTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void A_created_key_is_thirty_two_bytes_hex_and_owner_only()
    {
        var key = LinkKey.EnsureCreated(KeyPath);

        key.Should().HaveCount(32);
        File.ReadAllText(KeyPath).Trim().Should().Be(Convert.ToHexStringLower(key));
        if (!OperatingSystem.IsWindows())
            File.GetUnixFileMode(KeyPath)
                .Should()
                .Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [Fact]
    public void Creating_a_key_twice_keeps_the_first_one()
    {
        var first = LinkKey.EnsureCreated(KeyPath);
        var second = LinkKey.EnsureCreated(KeyPath);

        second.Should().Equal(first);
    }

    [Fact]
    public void A_missing_key_is_reported_as_null()
    {
        LinkKey.TryLoad(Path.Combine(_directory, "absent.key")).Should().BeNull();
    }

    [Fact]
    public async Task Frames_round_trip_in_order()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var buffer = new MemoryStream();
        var writer = new LinkFrameWriter(buffer, new LinkKeyRing(key));

        await writer.WriteAsync(
            LinkFrameKind.Request,
            new byte[] { 1, 2, 3 },
            CancellationToken.None
        );
        await writer.WriteAsync(
            LinkFrameKind.Response,
            new byte[] { 4, 5 },
            CancellationToken.None
        );

        var reader = new LinkFrameReader(new MemoryStream(buffer.ToArray()), new LinkKeyRing(key));

        var first = await reader.ReadAsync(CancellationToken.None);
        var second = await reader.ReadAsync(CancellationToken.None);

        first.Kind.Should().Be(LinkFrameKind.Request);
        first.Payload.Should().Equal(1, 2, 3);
        second.Kind.Should().Be(LinkFrameKind.Response);
        second.Payload.Should().Equal(4, 5);
    }

    [Fact]
    public async Task A_replayed_frame_is_rejected()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var buffer = new MemoryStream();
        await new LinkFrameWriter(buffer, new LinkKeyRing(key)).WriteAsync(
            LinkFrameKind.Request,
            new byte[] { 1, 2, 3 },
            CancellationToken.None
        );

        var bytes = buffer.ToArray();
        var firstLength = 4 + BinaryPrimitives.ReadInt32BigEndian(bytes);
        var replay = new byte[firstLength * 2];
        Buffer.BlockCopy(bytes, 0, replay, 0, firstLength);
        Buffer.BlockCopy(bytes, 0, replay, firstLength, firstLength);

        var reader = new LinkFrameReader(new MemoryStream(replay), new LinkKeyRing(key));
        await reader.ReadAsync(CancellationToken.None);

        var act = async () => await reader.ReadAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task A_tampered_frame_fails_authentication()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var buffer = new MemoryStream();
        await new LinkFrameWriter(buffer, new LinkKeyRing(key)).WriteAsync(
            LinkFrameKind.Request,
            new byte[] { 1, 2, 3 },
            CancellationToken.None
        );

        var bytes = buffer.ToArray();
        bytes[^1] ^= 0xff;
        var reader = new LinkFrameReader(new MemoryStream(bytes), new LinkKeyRing(key));

        var act = async () => await reader.ReadAsync(CancellationToken.None);

        await act.Should().ThrowAsync<AuthenticationTagMismatchException>();
    }

    [Fact]
    public async Task A_request_survives_a_session_key_rotation()
    {
        var key = LinkKey.EnsureCreated(KeyPath);
        await using var session = await StartServerAsync(key, (request, _) =>
        {
            var settings = request.Should().BeOfType<GetSettingsRequest>().Subject;
            settings.Id.Should().NotBeNullOrEmpty();
            return Task.FromResult(
                LinkResponse.Success(
                    settings.Id,
                    new SettingsDto(
                        [2],
                        [2, 4],
                        0.002,
                        new Dictionary<string, bool>(),
                        new WindowSizeDto(true, 0, 0)
                    )
                )
            );
        });

        await using var client = CreateClient(rotateSeconds: 0);

        var first = await client.GetSettingsAsync();
        first.DisplayLevels.Should().Equal(2);

        await client.RotateIfDueAsync();

        client.Rotations.Should().Be(1);
        session.Server.Rotations.Should().Be(1);

        var second = await client.GetSettingsAsync();
        second.ExtractLevels.Should().Equal(2, 4);
        second.Tolerance.Should().Be(0.002);
    }

    [Fact]
    public async Task A_missing_socket_reports_offline()
    {
        LinkKey.EnsureCreated(KeyPath);
        await using var client = CreateClient();

        var act = async () => await client.GetStateAsync();

        await act.Should()
            .ThrowAsync<LinkUnavailableException>()
            .Where(exception => exception.Code == LinkError.LauncherOffline);
    }

    [Fact]
    public async Task A_missing_key_reports_key_missing()
    {
        await using var client = CreateClient();

        var act = async () => await client.GetStateAsync();

        await act.Should()
            .ThrowAsync<LinkUnavailableException>()
            .Where(exception => exception.Code == LinkError.KeyMissing);
    }

    [Fact]
    public async Task A_second_server_on_the_same_socket_fails_to_bind()
    {
        var key = LinkKey.EnsureCreated(KeyPath);
        await using var session = await StartServerAsync(key, (request, _) =>
            Task.FromResult(LinkResponse.Success(request.Id))
        );

        var second = new LauncherLinkServer(
            SocketPath,
            key,
            TimeSpan.FromSeconds(5),
            (request, _) => Task.FromResult(LinkResponse.Success(request.Id)),
            TimeProvider.System,
            NullLogger<LauncherLinkServer>.Instance
        );

        var act = async () => await second.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<System.Net.Sockets.SocketException>();
    }

    [Fact]
    public async Task A_response_without_a_payload_is_a_protocol_error()
    {
        var key = LinkKey.EnsureCreated(KeyPath);
        await using var session = await StartServerAsync(key, (request, _) =>
            Task.FromResult(LinkResponse.Success(request.Id))
        );
        await using var client = CreateClient();

        var act = async () => await client.GetStateAsync();

        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task A_reconnect_attempt_is_throttled_briefly()
    {
        LinkKey.EnsureCreated(KeyPath);
        await using var client = CreateClient();

        var first = async () =>
            await client.NavigateAsync(GeoTools.Shared.Application.Views.ViewId.Map);
        var second = async () =>
            await client.NavigateAsync(GeoTools.Shared.Application.Views.ViewId.Map);

        await first.Should().ThrowAsync<LinkUnavailableException>();
        await second.Should().ThrowAsync<LinkUnavailableException>();
    }

    [Fact]
    public async Task A_failed_rotation_drops_the_session()
    {
        var key = LinkKey.EnsureCreated(KeyPath);
        var session = await StartServerAsync(key, (request, _) =>
            Task.FromResult(
                LinkResponse.Success(request.Id, new CommandResultDto(true, "ok", null, null))
            )
        );
        await using var client = CreateClient(rotateSeconds: 0);
        await client.NavigateAsync(GeoTools.Shared.Application.Views.ViewId.Map);

        await session.DisposeAsync();
        await Task.Delay(50);
        await client.RotateIfDueAsync();

        client.IsConnected.Should().BeFalse();
        await client.DisposeAsync();
        await client.DisposeAsync();
    }

    [Fact]
    public async Task A_frame_with_an_impossible_length_is_rejected()
    {
        var reader = new LinkFrameReader(
            new MemoryStream(new byte[4]),
            new LinkKeyRing(RandomNumberGenerator.GetBytes(32))
        );

        var act = async () => await reader.ReadAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task A_frame_with_an_unknown_version_is_rejected()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var buffer = new MemoryStream();
        await new LinkFrameWriter(buffer, new LinkKeyRing(key)).WriteAsync(
            LinkFrameKind.Request,
            new byte[] { 1 },
            CancellationToken.None
        );

        var bytes = buffer.ToArray();
        bytes[4] = 99;

        var act = async () =>
            await new LinkFrameReader(new MemoryStream(bytes), new LinkKeyRing(key)).ReadAsync(
                CancellationToken.None
            );

        await act.Should().ThrowAsync<InvalidDataException>();
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
        catch (IOException) { }

        return ValueTask.CompletedTask;
    }

    private LauncherLinkClient CreateClient(int rotateSeconds = 900) =>
        new(
            Options.Create(
                new LinkOptions
                {
                    SocketPath = SocketPath,
                    KeyPath = KeyPath,
                    RotateSeconds = rotateSeconds,
                    CommandTimeoutSeconds = 5,
                    HandshakeTimeoutSeconds = 5,
                }
            ),
            TimeProvider.System,
            NullLogger<LauncherLinkClient>.Instance
        );

    private async Task<RunningServer> StartServerAsync(
        byte[] key,
        LauncherLinkServer.RequestHandler handler
    )
    {
        var server = new LauncherLinkServer(
            SocketPath,
            key,
            TimeSpan.FromSeconds(5),
            handler,
            TimeProvider.System,
            NullLogger<LauncherLinkServer>.Instance
        );
        await server.StartAsync(CancellationToken.None);
        return new RunningServer(server);
    }

    private sealed class RunningServer(LauncherLinkServer server) : IAsyncDisposable
    {
        public LauncherLinkServer Server { get; } = server;

        public async ValueTask DisposeAsync() => await Server.DisposeAsync();
    }
}
