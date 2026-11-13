namespace GeoTools.Shared.Infrastructure.Persistence;

internal sealed class MessageRow
{
    public long Sequence { get; set; }
    public string Topic { get; set; } = string.Empty;
    public byte[] Body { get; set; } = [];
    public long PublishedAt { get; set; }
    public string Publisher { get; set; } = string.Empty;
    public string? CorrelationId { get; set; }
}

internal sealed class SubscriptionRow
{
    public string Subscriber { get; set; } = string.Empty;
    public string Topics { get; set; } = string.Empty;
    public long LastSequence { get; set; }
    public int Attempts { get; set; }
    public long? ParkedAt { get; set; }
    public string? LastError { get; set; }
}

internal sealed class RunRow
{
    public string Id { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public long StartedAt { get; set; }
    public long? FinishedAt { get; set; }
    public string? Error { get; set; }
}

internal sealed class StepRow
{
    public string RunId { get; set; } = string.Empty;
    public string StepKey { get; set; } = string.Empty;
    public int Ordinal { get; set; }
    public string Stage { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public double Fraction { get; set; }
    public long? BytesReceived { get; set; }
    public long? TotalBytes { get; set; }
    public double? BytesPerSecond { get; set; }
    public double? EtaSeconds { get; set; }
    public string? Error { get; set; }
}

internal sealed class ArtifactRow
{
    public string Key { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string? Region { get; set; }
    public string Path { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public long ModifiedAt { get; set; }
    public string Fingerprint { get; set; } = string.Empty;
    public string AdminLevels { get; set; } = string.Empty;
    public string? Bounds { get; set; }
    public string Status { get; set; } = string.Empty;
    public long UpdatedAt { get; set; }
}

internal sealed class GeometryCacheRow
{
    public string Key { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string AdminLevels { get; set; } = string.Empty;
    public double Tolerance { get; set; }
    public int CodecVersion { get; set; }
    public int PointCount { get; set; }
    public string Bounds { get; set; } = string.Empty;
    public byte[] Blob { get; set; } = [];
    public long CreatedAt { get; set; }
}

internal sealed class SettingRow
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public long UpdatedAt { get; set; }
}

internal sealed class FeatureFlagRow
{
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public long UpdatedAt { get; set; }
}

internal sealed class HandledRequestRow
{
    public string RequestId { get; set; } = string.Empty;
    public long HandledAt { get; set; }
}

internal sealed class RegionRow
{
    public string Id { get; set; } = string.Empty;
    public string SourceKey { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string OsmId { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? BorderType { get; set; }
    public int AdminLevel { get; set; }
    public string? ParentId { get; set; }
    public bool ParentResolved { get; set; }
    public double MinLongitude { get; set; }
    public double MinLatitude { get; set; }
    public double MaxLongitude { get; set; }
    public double MaxLatitude { get; set; }
    public double Area { get; set; }
    public int PointCount { get; set; }
    public byte[] Blob { get; set; } = [];
    public long CreatedAt { get; set; }
}

internal sealed class BorderLineRow
{
    public string Id { get; set; } = string.Empty;
    public string SourceKey { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public int AdminLevel { get; set; }
    public double MinLongitude { get; set; }
    public double MinLatitude { get; set; }
    public double MaxLongitude { get; set; }
    public double MaxLatitude { get; set; }
    public int PointCount { get; set; }
    public byte[] Blob { get; set; } = [];
    public long CreatedAt { get; set; }
}
