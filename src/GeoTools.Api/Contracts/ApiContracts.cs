using GeoTools.Shared.Application.Link;
using GeoTools.Shared.Application.Views;
using GeoTools.Shared.Domain.Geospatial;

namespace GeoTools.Api.Contracts;

public sealed record FlagDto(string Name, bool Enabled);

public sealed record SettingsViewDto(
    int[] DisplayLevels,
    int[] ExtractLevels,
    double Tolerance,
    IReadOnlyList<FlagDto> Flags,
    WindowSizeDto Window
);

public sealed record LocalStateDto(
    bool DatabaseAvailable,
    string? DatabaseError,
    SettingsViewDto Settings,
    RunDto? LatestRun,
    int ArtifactCount,
    int BorderFileCount,
    int[] AvailableAdminLevels
);

public sealed record StateResponse(
    AppStateDto? Launcher,
    string LauncherStatus,
    string? LauncherReason,
    LocalStateDto Local
);

public sealed record ArtifactDto(
    string Key,
    string Kind,
    string? Region,
    string Path,
    long SizeBytes,
    DateTimeOffset ModifiedAt,
    string Status,
    int[] AdminLevels
);

public sealed record StepDto(
    string Key,
    string Stage,
    string Label,
    string Status,
    int Attempts,
    double Fraction,
    string? Error
);

public sealed record RunDetailDto(RunDto Run, IReadOnlyList<StepDto> Steps);

public sealed record RegionDto(
    string Name,
    bool HasPbf,
    bool HasBorders,
    long PbfBytes,
    long BordersBytes,
    string? Status
);

public sealed record RegionsResponse(
    IReadOnlyList<RegionDto> Regions,
    int[] AvailableAdminLevels,
    GeoBounds Bounds,
    int PolylineCount,
    int PointCount,
    bool GeometryAvailable
);

public sealed record GeoJsonGeometry(string Type, IReadOnlyList<double[]> Coordinates);

public sealed record GeoJsonFeature(
    string Type,
    GeoJsonGeometry Geometry,
    int AdminLevel,
    int Index
);

public sealed record GeoJsonResponse(
    string Type,
    IReadOnlyList<GeoJsonFeature> Features,
    bool Truncated
);

public sealed record AdminLevelCountDto(int AdminLevel, int Count);

public sealed record BorderSummaryDto(
    int PolylineCount,
    int PointCount,
    IReadOnlyList<AdminLevelCountDto> CountsByLevel,
    int[] AdminLevels,
    GeoBounds Bounds,
    bool Truncated
);

public sealed record BorderQueryResponse(BorderSummaryDto Summary, GeoJsonResponse? GeoJson);

public sealed record ScreenshotInfoDto(
    string Base64Png,
    int Width,
    int Height,
    ViewId View,
    DateTimeOffset CapturedAt
);

public sealed record HealthResponse(
    string Status,
    string Launcher,
    bool DatabaseAvailable,
    DateTimeOffset CapturedAt
);

public sealed record NavigateBody(ViewId View);

public sealed record KeysBody(WindowKey[] Keys);

public sealed record SelectRowBody(string RowId);

public sealed record AdjustRowBody(string RowId, int Direction);

public sealed record ActivateRowBody(string RowId, bool Confirm);

public sealed record LevelsBody(int[] Levels);

public sealed record ToleranceBody(double Tolerance);

public sealed record WindowBody(bool Maximized, int Width, int Height);

public sealed record EnabledBody(bool Enabled);

public sealed record PipelineBody(PipelineAction Action, bool Confirm);

public sealed record FocusBody(double Longitude, double Latitude);

public sealed record FocusRegionBody(string RegionId);

public sealed record PanBody(double Dx, double Dy);

public sealed record ZoomBody(int Direction);

public sealed record BoundsInput(
    double MinLongitude,
    double MinLatitude,
    double MaxLongitude,
    double MaxLatitude
);

public sealed class BorderQueryTooLargeException(string message) : Exception(message);
