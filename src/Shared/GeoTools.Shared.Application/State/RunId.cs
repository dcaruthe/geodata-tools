namespace GeoTools.Shared.Application.State;

public readonly record struct RunId(Guid Value)
{
    public static RunId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("N");
}
