using System.Globalization;
using GeoTools.Shared.Application.Geodata;
using GeoTools.Shared.Application.Options;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Application.Storage;

namespace GeoTools.Shared.Infrastructure.Geodata;

internal sealed record RegionState(
    string Region,
    string SourcePath,
    bool SourceExists,
    long SourceSize,
    DateTimeOffset SourceModified,
    string BordersPath,
    string LevelsPath,
    bool BordersExists,
    long BordersSize,
    DateTimeOffset BordersModified,
    IReadOnlyList<int> SidecarLevels,
    bool SidecarReadable
);

internal enum BootstrapAction
{
    Load,
    Pull,
    Extract,
    ClearCache,
}

internal sealed record PlannedStep(
    StepKey Key,
    BootstrapAction Action,
    string Stage,
    string Label,
    RegionState? Region,
    bool ForcePull
);

internal sealed record BootstrapPlan(IReadOnlyList<PlannedStep> Steps);

internal static class BootstrapPlanner
{
    public static IReadOnlyList<RegionState> Scan(GeodataOptions options)
    {
        var root = RepositoryPaths.Resolve(options.RootPath);
        var bordersDirectory = Path.Combine(root, options.BordersSubdirectory);
        var states = new List<RegionState>();

        foreach (var region in options.Regions)
        {
            var sourcePath = Path.Combine(
                root,
                options.SourceFilePattern.Replace("{region}", region, StringComparison.Ordinal)
            );
            var bordersPath = Path.Combine(bordersDirectory, $"{region}-boundaries.geojsonseq");
            var levelsPath = Path.Combine(bordersDirectory, $"{region}-boundaries.levels");
            var source = new FileInfo(sourcePath);
            var borders = new FileInfo(bordersPath);
            var (sidecarLevels, sidecarReadable) = ReadSidecar(levelsPath);

            states.Add(
                new RegionState(
                    region,
                    sourcePath,
                    source.Exists,
                    source.Exists ? source.Length : 0,
                    source.Exists
                        ? new DateTimeOffset(source.LastWriteTimeUtc, TimeSpan.Zero)
                        : DateTimeOffset.UnixEpoch,
                    bordersPath,
                    levelsPath,
                    borders.Exists,
                    borders.Exists ? borders.Length : 0,
                    borders.Exists
                        ? new DateTimeOffset(borders.LastWriteTimeUtc, TimeSpan.Zero)
                        : DateTimeOffset.UnixEpoch,
                    sidecarLevels,
                    sidecarReadable
                )
            );
        }

        return states;
    }

    public static BootstrapPlan Plan(
        BootstrapRequest request,
        IReadOnlyList<RegionState> regions,
        IReadOnlyDictionary<ArtifactKey, ArtifactRecord> artifacts,
        IReadOnlyList<int> levels
    )
    {
        var selected = (
            request.Region is null
                ? regions
                : regions.Where(region => region.Region == request.Region)
        ).ToArray();
        var steps = new List<PlannedStep>();

        if (request.Scope == RebuildScope.GeometryCache)
        {
            steps.Add(
                new PlannedStep(
                    new StepKey("cache:clear"),
                    BootstrapAction.ClearCache,
                    "cache",
                    "Clearing the geometry cache",
                    null,
                    false
                )
            );
        }

        steps.Add(
            new PlannedStep(
                new StepKey("load"),
                BootstrapAction.Load,
                "load",
                "Loading border data",
                null,
                false
            )
        );

        var pullSteps = new List<PlannedStep>();
        if (request.Scope is RebuildScope.All or RebuildScope.Pull)
        {
            foreach (var region in selected)
            {
                var needed =
                    request.Scope == RebuildScope.Pull || request.Force || !region.SourceExists;
                if (!needed)
                    continue;

                pullSteps.Add(
                    new PlannedStep(
                        new StepKey($"pull:{region.Region}"),
                        BootstrapAction.Pull,
                        "pull",
                        $"Downloading {region.Region}",
                        region,
                        request.Force
                    )
                );
            }
        }

        steps.AddRange(pullSteps);

        var extractSteps = new List<PlannedStep>();
        if (request.Scope is RebuildScope.All or RebuildScope.Pull or RebuildScope.Extract)
        {
            foreach (var region in selected)
            {
                var needed = request.Scope switch
                {
                    RebuildScope.Extract => true,
                    RebuildScope.Pull => true,
                    _ => request.Force
                        || !IsBordersCurrent(
                            region,
                            artifacts.GetValueOrDefault(ArtifactKeys.Borders(region.Region)),
                            levels
                        ),
                };

                if (!needed)
                    continue;

                extractSteps.Add(
                    new PlannedStep(
                        new StepKey($"extract:{region.Region}"),
                        BootstrapAction.Extract,
                        "extract",
                        $"Extracting {region.Region}",
                        region,
                        false
                    )
                );
            }
        }

        steps.AddRange(extractSteps);

        if (pullSteps.Count > 0 || extractSteps.Count > 0)
        {
            steps.Add(
                new PlannedStep(
                    new StepKey("load:refresh"),
                    BootstrapAction.Load,
                    "load",
                    "Reloading borders",
                    null,
                    false
                )
            );
        }

        return new BootstrapPlan(steps);
    }

    public static bool IsBordersCurrent(
        RegionState region,
        ArtifactRecord? artifact,
        IReadOnlyList<int> levels
    )
    {
        if (!region.BordersExists || !region.SourceExists)
            return false;
        if (region.BordersModified < region.SourceModified)
            return false;

        if (artifact is not null)
        {
            return artifact.Status == ArtifactStatus.Current
                && artifact.SizeBytes == region.BordersSize
                && artifact.AdminLevels.SequenceEqual(levels.Order().Distinct());
        }

        return region.SidecarReadable
            && region.SidecarLevels.SequenceEqual(levels.Order().Distinct());
    }

    private static (IReadOnlyList<int> Levels, bool Readable) ReadSidecar(string path)
    {
        try
        {
            if (!File.Exists(path))
                return ([], false);

            var levels = File.ReadAllText(path)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value =>
                    int.TryParse(
                        value,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var level
                    )
                        ? level
                        : -1
                )
                .ToArray();

            return levels.Any(level => level < 0) ? ([], false) : (levels, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ([], false);
        }
    }
}
