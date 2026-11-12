namespace GeoTools.Shared.Application.State;

public readonly record struct StepKey(string Value)
{
    public override string ToString() => Value;
}
