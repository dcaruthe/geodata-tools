using GeoTools.Shared.Application.State;

namespace GeoTools.Shared.Infrastructure.Geodata;

internal static class ArtifactKeys
{
    public static ArtifactKey Pbf(string region) => new($"pbf:{region}");

    public static ArtifactKey Borders(string region) => new($"borders:{region}");
}
