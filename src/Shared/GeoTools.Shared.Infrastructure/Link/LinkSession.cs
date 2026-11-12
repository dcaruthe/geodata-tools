using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GeoTools.Shared.Application.Link;

namespace GeoTools.Shared.Infrastructure.Link;

internal sealed class LinkSession : IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly LinkKeyRing _ring;
    private readonly LinkFrameReader _reader;
    private readonly LinkFrameWriter _writer;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _rotations;

    private LinkSession(
        Stream stream,
        LinkKeyRing ring,
        LinkFrameReader reader,
        LinkFrameWriter writer
    )
    {
        _stream = stream;
        _ring = ring;
        _reader = reader;
        _writer = writer;
    }

    public int Rotations => Volatile.Read(ref _rotations);

    public static async Task<LinkSession> ConnectAsync(
        Stream stream,
        byte[] fileKey,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        using var scope = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        scope.CancelAfter(timeout);

        var fileRing = new LinkKeyRing(fileKey);
        var writer = new LinkFrameWriter(stream, fileRing);
        var reader = new LinkFrameReader(stream, fileRing);

        var clientNonce = RandomNumberGenerator.GetBytes(32);
        await writer.WriteAsync(
            LinkFrameKind.Hello,
            Payload(LinkProtocol.Version, clientNonce),
            scope.Token
        );

        var (kind, payload) = await reader.ReadAsync(scope.Token);
        if (kind != LinkFrameKind.Welcome)
            throw new InvalidDataException($"Expected a welcome frame, received {kind}.");

        var serverNonce = ReadNonce(payload);
        var ring = SessionRing(fileKey, clientNonce, serverNonce);
        return new LinkSession(
            stream,
            ring,
            new LinkFrameReader(stream, ring),
            new LinkFrameWriter(stream, ring)
        );
    }

    public static async Task<LinkSession> AcceptAsync(
        Stream stream,
        byte[] fileKey,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        using var scope = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        scope.CancelAfter(timeout);

        var fileRing = new LinkKeyRing(fileKey);
        var writer = new LinkFrameWriter(stream, fileRing);
        var reader = new LinkFrameReader(stream, fileRing);

        var (kind, payload) = await reader.ReadAsync(scope.Token);
        if (kind != LinkFrameKind.Hello)
            throw new InvalidDataException($"Expected a hello frame, received {kind}.");

        var clientNonce = ReadNonce(payload);
        var serverNonce = RandomNumberGenerator.GetBytes(32);
        await writer.WriteAsync(
            LinkFrameKind.Welcome,
            Payload(LinkProtocol.Version, serverNonce),
            scope.Token
        );

        var ring = SessionRing(fileKey, clientNonce, serverNonce);
        return new LinkSession(
            stream,
            ring,
            new LinkFrameReader(stream, ring),
            new LinkFrameWriter(stream, ring)
        );
    }

    public async Task<LinkResponse> SendAsync(
        LinkRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var scope = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            scope.CancelAfter(timeout);

            await _writer.WriteAsync(
                LinkFrameKind.Request,
                JsonSerializer.SerializeToUtf8Bytes<LinkRequest>(request, LinkJson.Options),
                scope.Token
            );

            var (kind, payload) = await _reader.ReadAsync(scope.Token);
            if (kind != LinkFrameKind.Response)
                throw new InvalidDataException($"Expected a response frame, received {kind}.");

            return JsonSerializer.Deserialize<LinkResponse>(payload, LinkJson.Options)
                ?? throw new InvalidDataException("The response frame was empty.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RotateAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var scope = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            scope.CancelAfter(timeout);

            var nextKey = RandomNumberGenerator.GetBytes(LinkProtocol.KeyBytes);
            await _writer.WriteAsync(LinkFrameKind.Rotate, nextKey, scope.Token);
            _ring.Key = nextKey;

            var (kind, _) = await _reader.ReadAsync(scope.Token);
            if (kind != LinkFrameKind.RotateAck)
                throw new InvalidDataException($"Expected a rotation ack, received {kind}.");

            Interlocked.Increment(ref _rotations);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal Task<(LinkFrameKind Kind, byte[] Payload)> ReadFrameAsync(
        CancellationToken cancellationToken
    ) => _reader.ReadAsync(cancellationToken);

    internal async Task WriteResponseAsync(
        LinkResponse response,
        CancellationToken cancellationToken
    ) =>
        await _writer.WriteAsync(
            LinkFrameKind.Response,
            JsonSerializer.SerializeToUtf8Bytes(response, LinkJson.Options),
            cancellationToken
        );

    internal void SetKey(byte[] key)
    {
        if (key.Length != LinkProtocol.KeyBytes)
            throw new InvalidDataException(
                $"A rotation key must be {LinkProtocol.KeyBytes} bytes, not {key.Length}."
            );

        _ring.Key = key;
    }

    internal async Task WriteControlAsync(
        LinkFrameKind kind,
        CancellationToken cancellationToken
    ) => await _writer.WriteAsync(kind, ReadOnlyMemory<byte>.Empty, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _writer.WriteAsync(
                LinkFrameKind.Close,
                ReadOnlyMemory<byte>.Empty,
                CancellationToken.None
            );
        }
        catch (Exception exception)
            when (exception is IOException or SocketException or ObjectDisposedException) { }

        await _stream.DisposeAsync();
        _gate.Dispose();
    }

    private static byte[] Payload(int version, byte[] nonce)
    {
        var payload = new byte[1 + nonce.Length];
        payload[0] = (byte)version;
        nonce.CopyTo(payload, 1);
        return payload;
    }

    private static byte[] ReadNonce(byte[] payload)
    {
        if (payload.Length != 1 + 32)
            throw new InvalidDataException($"A handshake frame is {payload.Length} bytes.");

        if (payload[0] != LinkProtocol.Version)
            throw new InvalidDataException($"Link protocol version {payload[0]} is not supported.");

        return payload[1..];
    }

    private static LinkKeyRing SessionRing(byte[] fileKey, byte[] clientNonce, byte[] serverNonce)
    {
        var salt = new byte[clientNonce.Length + serverNonce.Length];
        clientNonce.CopyTo(salt, 0);
        serverNonce.CopyTo(salt, clientNonce.Length);

        var key = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            fileKey,
            LinkProtocol.KeyBytes,
            salt,
            Encoding.UTF8.GetBytes(LinkProtocol.SessionInfo)
        );
        return new LinkKeyRing(key);
    }
}
