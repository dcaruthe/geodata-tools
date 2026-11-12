using GeoTools.Shared.Domain.Geospatial;

namespace GeoTools.Shared.Application.Messaging.Messages;

[MessageTopic(MessageTopics.BordersAvailable)]
public sealed record BordersAvailable(
    string CacheKey,
    int FeatureCount,
    IReadOnlyList<int> AdminLevels,
    GeoBounds Bounds,
    DateTimeOffset LoadedAt
);
