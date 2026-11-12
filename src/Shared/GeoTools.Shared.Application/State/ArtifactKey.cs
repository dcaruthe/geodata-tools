namespace GeoTools.Shared.Application.State;

public readonly record struct ArtifactKey(string Value)
{
    public override string ToString() => Value;
}
