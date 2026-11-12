namespace GeoTools.Shared.Application.State;

public sealed record GeodataSettings(
    IReadOnlyList<int> ExtractAdminLevels,
    double SimplifyToleranceDegrees
);
