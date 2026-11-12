using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text.Json;
using GeoTools.Shared.Application.Link;
using Microsoft.Extensions.Logging;

namespace GeoTools.Shared.Infrastructure.Link;

public sealed class LauncherLinkServer : IAsyncDisposable
{
    public delegate Task<LinkResponse> RequestHandler(
        LinkRequest request,
        CancellationToken cancellationToken
    );

    private readonly string _configuredSocketPath;
    private readonly byte[] _key;
    private readonly TimeSpan _handshakeTimeout;
    private readonly RequestHandler _handler;
    private readonly TimeProvider _clock;
    private readonly ILogger<LauncherLinkServer> _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<long, Task> _connections = new();
    private Socket? _listener;
    private Task? _acceptLoop;
    private long _connectionSequence;
    private int _activeConnections;
    private int _rotations;
    private long _lastRotatedAt;
    private int _stopped;

    public LauncherLinkServer(
        string socketPath,
        byte[] key,
        TimeSpan handshakeTimeout,
        RequestHandler handler,
        TimeProvider clock,
        ILogger<LauncherLinkServer> logger
    )
    {
        _configuredSocketPath = socketPath;
        _key = key;
        _handshakeTimeout = handshakeTimeout;
        _handler = handler;
        _clock = clock;
        _logger = logger;
    }

    public string SocketPath { get; private set; } = string.Empty;

    public int ActiveConnections => Volatile.Read(ref _activeConnections);

    public int Rotations => Volatile.Read(ref _rotations);

    public DateTimeOffset? LastRotatedAt =>
        Volatile.Read(ref _lastRotatedAt) is var value && value > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(value)
            : null;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        SocketPath = LinkKey.ResolvePath(_configuredSocketPath);
        var directory = Path.GetDirectoryName(SocketPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        // A crashed launcher leaves the socket file behind. A live one answers its backlog.
        if (Path.Exists(SocketPath) && !CanConnect(SocketPath))
            File.Delete(SocketPath);

        var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(SocketPath));
        LinkKey.RestrictToOwner(SocketPath);
        listener.Listen(8);
        _listener = listener;
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token), CancellationToken.None);
        _logger.LogInformation("Link listening at {SocketPath}.", SocketPath);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 1)
            return;

        await _cts.CancelAsync();
        _listener?.Dispose();

        try
        {
            if (_acceptLoop is not null)
                await _acceptLoop.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        }
        catch (Exception exception)
            when (exception is TimeoutException or OperationCanceledException)
        {
            _logger.LogWarning("The link listener did not stop within five seconds.");
        }

        try
        {
            await Task.WhenAll(_connections.Values)
                .WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        }
        catch (Exception exception)
            when (exception is TimeoutException or OperationCanceledException)
        {
            _logger.LogWarning("A link connection did not close within five seconds.");
        }

        if (Path.Exists(SocketPath))
            File.Delete(SocketPath);

        _logger.LogInformation("Link stopped.");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        _cts.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Socket socket;
            try
            {
                socket = await _listener!.AcceptAsync(cancellationToken);
            }
            catch (Exception exception)
                when (exception is OperationCanceledException or ObjectDisposedException)
            {
                return;
            }
            catch (SocketException exception)
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                _logger.LogWarning(exception, "The link listener stopped accepting connections.");
                return;
            }

            var id = Interlocked.Increment(ref _connectionSequence);
            Interlocked.Increment(ref _activeConnections);
            _connections[id] = HandleAsync(socket, cancellationToken);
            _ = _connections[id]
                .ContinueWith(
                    completed => _connections.TryRemove(id, out _),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default
                );
        }
    }

    private async Task HandleAsync(Socket socket, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new NetworkStream(socket, ownsSocket: true);
            await using var session = await LinkSession.AcceptAsync(
                stream,
                _key,
                _handshakeTimeout,
                cancellationToken
            );

            while (!cancellationToken.IsCancellationRequested)
            {
                var (kind, payload) = await session.ReadFrameAsync(cancellationToken);
                switch (kind)
                {
                    case LinkFrameKind.Request:
                        await HandleRequestAsync(session, payload, cancellationToken);
                        break;
                    case LinkFrameKind.Rotate:
                        session.SetKey(payload);
                        Interlocked.Increment(ref _rotations);
                        Interlocked.Exchange(
                            ref _lastRotatedAt,
                            _clock.GetUtcNow().ToUnixTimeMilliseconds()
                        );
                        await session.WriteControlAsync(LinkFrameKind.RotateAck, cancellationToken);
                        _logger.LogInformation("Accepted a link session key rotation.");
                        break;
                    case LinkFrameKind.Close:
                        return;
                    default:
                        throw new InvalidDataException($"Unexpected {kind} frame on an open link.");
                }
            }
        }
        catch (Exception exception)
            when (exception is OperationCanceledException or ObjectDisposedException) { }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "A link connection closed.");
        }
        finally
        {
            socket.Dispose();
            Interlocked.Decrement(ref _activeConnections);
        }
    }

    private async Task HandleRequestAsync(
        LinkSession session,
        byte[] payload,
        CancellationToken cancellationToken
    )
    {
        LinkResponse response;
        try
        {
            var request = JsonSerializer.Deserialize<LinkRequest>(payload, LinkJson.Options)
                ?? throw new InvalidDataException("The request frame was empty.");
            response = await _handler(request, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "A link request failed.");
            response = LinkResponse.Failure(
                string.Empty,
                new LinkError(LinkError.Internal, exception.Message)
            );
        }

        await session.WriteResponseAsync(response, cancellationToken);
    }

    private static bool CanConnect(string path)
    {
        try
        {
            using var probe = new Socket(
                AddressFamily.Unix,
                SocketType.Stream,
                ProtocolType.Unspecified
            );
            probe.Connect(new UnixDomainSocketEndPoint(path));
            return true;
        }
        catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
        {
            return false;
        }
    }
}
