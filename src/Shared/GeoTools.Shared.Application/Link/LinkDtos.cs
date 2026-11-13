using GeoTools.Shared.Application.Views;

namespace GeoTools.Shared.Application.Link;

public sealed record ProgressDto(
    string Phase,
    string Stage,
    string Label,
    string? Detail,
    double Fraction,
    double? EtaSeconds,
    string? Error,
    DateTimeOffset UpdatedAt
);

public sealed record RunDto(
    string Id,
    string Reason,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string? Error
);

public sealed record LinkStatusDto(
    bool Enabled,
    int Connections,
    int Rotations,
    int RotateSeconds,
    DateTimeOffset? LastRotatedAt
);

public sealed record DiagnosticsDto(
    int ProcessId,
    long ManagedMemoryBytes,
    int ThreadCount,
    string DatabasePath,
    string GeodataRoot,
    string SocketPath,
    string KeyPath
);

public sealed record AppStateDto(
    string Version,
    int ProtocolVersion,
    DateTimeOffset CapturedAt,
    DateTimeOffset ProcessStartedAt,
    long UptimeSeconds,
    ViewId ActiveView,
    string WindowTitle,
    int WindowWidth,
    int WindowHeight,
    bool WindowMaximized,
    ProgressDto Progress,
    RunDto? LatestRun,
    LinkStatusDto Link,
    DiagnosticsDto Diagnostics
);

public sealed record LogEntryDto(
    long Sequence,
    DateTimeOffset Timestamp,
    string Level,
    string Category,
    string Message,
    string? Exception
);

public sealed record LogPageDto(IReadOnlyList<LogEntryDto> Entries, long NextSequence);

public sealed record MaintenanceRowDto(
    string Id,
    string Label,
    string Value,
    string Kind,
    bool Selectable,
    bool Enabled,
    bool Destructive
);

public sealed record MaintenanceDto(
    IReadOnlyList<MaintenanceRowDto> Rows,
    string? SelectedRowId,
    string Status,
    string? PendingConfirmation
);

public sealed record SettingsDto(
    int[] DisplayLevels,
    int[] ExtractLevels,
    double Tolerance,
    IReadOnlyDictionary<string, bool> Flags,
    WindowSizeDto Window
);

public sealed record WindowSizeDto(bool Maximized, int Width, int Height);

public sealed record ScreenshotDto(
    byte[] Png,
    int Width,
    int Height,
    ViewId View,
    DateTimeOffset CapturedAt,
    string Mode
);

public sealed record CommandResultDto(
    bool Accepted,
    string Status,
    ViewId? ActiveView,
    string? Error
)
{
    public static CommandResultDto Ok(string status, ViewId? activeView = null) =>
        new(true, status, activeView, null);

    public static CommandResultDto Failed(string error) => new(false, string.Empty, null, error);
}
