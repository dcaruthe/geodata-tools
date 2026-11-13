using System.Net.Sockets;
using System.Text.Json;
using GeoTools.Shared.Application.Link;
using GeoTools.Shared.Application.Views;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeoTools.Shared.Infrastructure.Link;

public sealed class LauncherLinkClient(
    IOptions<LinkOptions> options,
    TimeProvider clock,
    ILogger<LauncherLinkClient> logger
) : ILauncherLink
{
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private LinkSession? _session;
    private DateTimeOffset _nextAttempt = DateTimeOffset.MinValue;
    private DateTimeOffset _connectedAt = DateTimeOffset.MinValue;
    private DateTimeOffset? _lastRotatedAt;
    private int _rotations;

    public bool IsConnected => Volatile.Read(ref _session) is not null;

    public int Rotations => Volatile.Read(ref _rotations);

    public DateTimeOffset? LastRotatedAt => _lastRotatedAt;

    public async Task<AppStateDto> GetStateAsync(CancellationToken cancellationToken = default) =>
        Payload<AppStateDto>(await SendAsync(new GetStateRequest(NewId()), cancellationToken));

    public async Task<LogPageDto> GetLogsAsync(
        string? level,
        int limit,
        long? after,
        CancellationToken cancellationToken = default
    ) =>
        Payload<LogPageDto>(
            await SendAsync(new GetLogsRequest(NewId(), level, limit, after), cancellationToken)
        );

    public async Task<MaintenanceDto> GetMaintenanceAsync(
        CancellationToken cancellationToken = default
    ) =>
        Payload<MaintenanceDto>(
            await SendAsync(new GetMaintenanceRequest(NewId()), cancellationToken)
        );

    public async Task<SettingsDto> GetSettingsAsync(
        CancellationToken cancellationToken = default
    ) => Payload<SettingsDto>(await SendAsync(new GetSettingsRequest(NewId()), cancellationToken));

    public async Task<ScreenshotDto> ScreenshotAsync(
        int? width,
        int? height,
        ScreenshotMode mode = ScreenshotMode.Scene,
        CancellationToken cancellationToken = default
    ) =>
        Payload<ScreenshotDto>(
            await SendAsync(new ScreenshotRequest(NewId(), width, height, mode), cancellationToken)
        );

    public async Task<CommandResultDto> NavigateAsync(
        ViewId view,
        CancellationToken cancellationToken = default
    ) =>
        Payload<CommandResultDto>(
            await SendAsync(new NavigateRequest(NewId(), view), cancellationToken)
        );

    public async Task<CommandResultDto> PressKeysAsync(
        IReadOnlyList<WindowKey> keys,
        CancellationToken cancellationToken = default
    ) =>
        Payload<CommandResultDto>(
            await SendAsync(new PressKeysRequest(NewId(), keys.ToArray()), cancellationToken)
        );

    public async Task<CommandResultDto> MaintenanceAsync(
        MaintenanceOperation operation,
        string? rowId,
        int direction = 0,
        bool confirm = false,
        CancellationToken cancellationToken = default
    ) =>
        Payload<CommandResultDto>(
            await SendAsync(
                new MaintenanceCommandRequest(NewId(), operation, rowId, direction, confirm),
                cancellationToken
            )
        );

    public async Task<CommandResultDto> PipelineAsync(
        PipelineAction action,
        bool confirm = false,
        CancellationToken cancellationToken = default
    ) =>
        Payload<CommandResultDto>(
            await SendAsync(new PipelineCommandRequest(NewId(), action, confirm), cancellationToken)
        );

    public async Task<CommandResultDto> SetDisplayLevelsAsync(
        IReadOnlyList<int> levels,
        CancellationToken cancellationToken = default
    ) =>
        Payload<CommandResultDto>(
            await SendAsync(
                new SetDisplayLevelsRequest(NewId(), levels.ToArray()),
                cancellationToken
            )
        );

    public async Task<CommandResultDto> SetExtractLevelsAsync(
        IReadOnlyList<int> levels,
        CancellationToken cancellationToken = default
    ) =>
        Payload<CommandResultDto>(
            await SendAsync(
                new SetExtractLevelsRequest(NewId(), levels.ToArray()),
                cancellationToken
            )
        );

    public async Task<CommandResultDto> SetToleranceAsync(
        double tolerance,
        CancellationToken cancellationToken = default
    ) =>
        Payload<CommandResultDto>(
            await SendAsync(new SetToleranceRequest(NewId(), tolerance), cancellationToken)
        );

    public async Task<CommandResultDto> SetFeatureFlagAsync(
        string flag,
        bool enabled,
        CancellationToken cancellationToken = default
    ) =>
        Payload<CommandResultDto>(
            await SendAsync(new SetFeatureFlagRequest(NewId(), flag, enabled), cancellationToken)
        );

    public async Task<CommandResultDto> SetWindowSizeAsync(
        bool maximized,
        int width,
        int height,
        CancellationToken cancellationToken = default
    ) =>
        Payload<CommandResultDto>(
            await SendAsync(
                new SetWindowSizeRequest(NewId(), maximized, width, height),
                cancellationToken
            )
        );

    public async Task<CommandResultDto> ResetSettingsAsync(
        CancellationToken cancellationToken = default
    ) =>
        Payload<CommandResultDto>(
            await SendAsync(new ResetSettingsRequest(NewId()), cancellationToken)
        );

    public async Task RotateIfDueAsync(CancellationToken cancellationToken = default)
    {
        var session = Volatile.Read(ref _session);
        if (session is null)
            return;

        if ((clock.GetUtcNow() - _connectedAt).TotalSeconds < options.Value.RotateSeconds)
            return;

        try
        {
            await session.RotateAsync(Timeout, cancellationToken);
            Interlocked.Exchange(ref _rotations, session.Rotations);
            _lastRotatedAt = clock.GetUtcNow();
            _connectedAt = clock.GetUtcNow();
            logger.LogInformation(
                "Rotated the link session key ({Rotations} rotations).",
                _rotations
            );
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or SocketException
                        or InvalidDataException
                        or OperationCanceledException
                        or ObjectDisposedException
            )
        {
            logger.LogWarning(exception, "Could not rotate the link session key; reconnecting.");
            await DropAsync();
        }
    }

    public async ValueTask DisposeAsync() => await DropAsync();

    private TimeSpan Timeout => TimeSpan.FromSeconds(options.Value.CommandTimeoutSeconds);

    private async Task<LinkResponse> SendAsync(
        LinkRequest request,
        CancellationToken cancellationToken
    )
    {
        var session = await EnsureAsync(cancellationToken);
        try
        {
            var response = await session.SendAsync(request, Timeout, cancellationToken);
            if (!response.Ok)
                throw new LinkUnavailableException(
                    response.Error?.Code ?? LinkError.Rejected,
                    response.Error?.Message ?? "The launcher rejected the request."
                );

            return response;
        }
        catch (LinkUnavailableException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or SocketException
                        or InvalidDataException
                        or OperationCanceledException
                        or ObjectDisposedException
            )
        {
            await DropAsync();
            throw new LinkUnavailableException(
                LinkError.LauncherOffline,
                $"The launcher link failed: {exception.Message}"
            );
        }
    }

    private async Task<LinkSession> EnsureAsync(CancellationToken cancellationToken)
    {
        var current = Volatile.Read(ref _session);
        if (current is not null)
            return current;

        await _connectGate.WaitAsync(cancellationToken);
        try
        {
            current = _session;
            if (current is not null)
                return current;

            var now = clock.GetUtcNow();
            if (now < _nextAttempt)
                throw new LinkUnavailableException(
                    LinkError.LauncherOffline,
                    "The launcher link is not available yet. Start the launcher and retry."
                );

            var key = LinkKey.TryLoad(options.Value.KeyPath)
                ?? throw new LinkUnavailableException(
                    LinkError.KeyMissing,
                    "The link key has not been created. Start the launcher once, then retry."
                );

            var socketPath = LinkKey.ResolvePath(options.Value.SocketPath);
            if (!Path.Exists(socketPath))
            {
                _nextAttempt = now.AddSeconds(1);
                throw new LinkUnavailableException(
                    LinkError.LauncherOffline,
                    $"No launcher is listening at {socketPath}. Start the launcher and retry."
                );
            }

            var socket = new Socket(
                AddressFamily.Unix,
                SocketType.Stream,
                ProtocolType.Unspecified
            );
            try
            {
                using var scope = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken
                );
                scope.CancelAfter(TimeSpan.FromSeconds(options.Value.HandshakeTimeoutSeconds));
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), scope.Token);
                var stream = new NetworkStream(socket, ownsSocket: true);
                var session = await LinkSession.ConnectAsync(
                    stream,
                    key,
                    TimeSpan.FromSeconds(options.Value.HandshakeTimeoutSeconds),
                    scope.Token
                );

                Volatile.Write(ref _session, session);
                _nextAttempt = DateTimeOffset.MinValue;
                _connectedAt = clock.GetUtcNow();
                logger.LogInformation("Connected to the launcher at {SocketPath}.", socketPath);
                return session;
            }
            catch (Exception exception)
            {
                socket.Dispose();
                _nextAttempt = now.AddSeconds(1);
                throw new LinkUnavailableException(
                    LinkError.LauncherOffline,
                    $"Could not connect to the launcher at {socketPath}: {exception.Message}"
                );
            }
        }
        finally
        {
            _connectGate.Release();
        }
    }

    private async Task DropAsync()
    {
        var session = Interlocked.Exchange(ref _session, null);
        if (session is null)
            return;

        try
        {
            await session.DisposeAsync();
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException) { }

        logger.LogInformation("Disconnected from the launcher.");
    }

    private static T Payload<T>(LinkResponse response) =>
        response.ReadPayload<T>()
        ?? throw new InvalidDataException(
            $"The launcher response did not contain {typeof(T).Name}."
        );

    private static string NewId() => Guid.NewGuid().ToString("N");
}
