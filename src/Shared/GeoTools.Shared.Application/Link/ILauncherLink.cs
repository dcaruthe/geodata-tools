using GeoTools.Shared.Application.Views;

namespace GeoTools.Shared.Application.Link;

// The API's only way to see or drive the running window. Every method throws
// LinkUnavailableException when the launcher is not reachable.
public interface ILauncherLink
{
    bool IsConnected { get; }

    int Rotations { get; }

    DateTimeOffset? LastRotatedAt { get; }

    Task<AppStateDto> GetStateAsync(CancellationToken cancellationToken = default);

    Task<LogPageDto> GetLogsAsync(
        string? level,
        int limit,
        long? after,
        CancellationToken cancellationToken = default
    );

    Task<MaintenanceDto> GetMaintenanceAsync(CancellationToken cancellationToken = default);

    Task<SettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default);

    Task<ScreenshotDto> ScreenshotAsync(
        int? width,
        int? height,
        ScreenshotMode mode = ScreenshotMode.Scene,
        CancellationToken cancellationToken = default
    );

    Task<CommandResultDto> NavigateAsync(
        ViewId view,
        CancellationToken cancellationToken = default
    );

    Task<CommandResultDto> PressKeysAsync(
        IReadOnlyList<WindowKey> keys,
        CancellationToken cancellationToken = default
    );

    Task<CommandResultDto> MaintenanceAsync(
        MaintenanceOperation operation,
        string? rowId,
        int direction = 0,
        bool confirm = false,
        CancellationToken cancellationToken = default
    );

    Task<CommandResultDto> PipelineAsync(
        PipelineAction action,
        bool confirm = false,
        CancellationToken cancellationToken = default
    );

    Task<CommandResultDto> SetDisplayLevelsAsync(
        IReadOnlyList<int> levels,
        CancellationToken cancellationToken = default
    );

    Task<CommandResultDto> SetExtractLevelsAsync(
        IReadOnlyList<int> levels,
        CancellationToken cancellationToken = default
    );

    Task<CommandResultDto> SetToleranceAsync(
        double tolerance,
        CancellationToken cancellationToken = default
    );

    Task<CommandResultDto> SetFeatureFlagAsync(
        string flag,
        bool enabled,
        CancellationToken cancellationToken = default
    );

    Task<CommandResultDto> ResetSettingsAsync(CancellationToken cancellationToken = default);

    Task RotateIfDueAsync(CancellationToken cancellationToken = default);
}
