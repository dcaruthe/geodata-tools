namespace GeoTools.Shared.Application.Geodata;

public sealed record BootstrapRequest(
    string Reason,
    RebuildScope Scope = RebuildScope.All,
    bool Force = false,
    string? Region = null
)
{
    public static BootstrapRequest Startup { get; } = new("startup");
}
