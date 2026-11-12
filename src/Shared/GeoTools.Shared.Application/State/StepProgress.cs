namespace GeoTools.Shared.Application.State;

public readonly record struct StepProgress(
    double Fraction,
    long? BytesReceived = null,
    long? TotalBytes = null,
    double? BytesPerSecond = null,
    double? EtaSeconds = null
)
{
    public static StepProgress Zero { get; } = default;

    public StepProgress Clamped() => this with { Fraction = Math.Clamp(Fraction, 0, 1) };
}
