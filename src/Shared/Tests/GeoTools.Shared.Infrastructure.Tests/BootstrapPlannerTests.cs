using FluentAssertions;
using GeoTools.Shared.Application.Geodata;
using GeoTools.Shared.Application.Options;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Infrastructure.Geodata;

namespace GeoTools.Shared.Infrastructure.Tests;

public sealed class BootstrapPlannerTests
{
    [Fact]
    public void Plan_for_a_missing_region_pulls_extracts_and_refreshes()
    {
        var plan = BootstrapPlanner.Plan(
            new BootstrapRequest("test"),
            [State("africa", source: false, border: false)],
            new Dictionary<ArtifactKey, ArtifactRecord>(),
            [2, 4]
        );

        Keys(plan).Should().Equal("load", "pull:africa", "extract:africa", "load:refresh");
    }

    [Fact]
    public void Plan_for_current_legacy_files_only_loads()
    {
        var plan = BootstrapPlanner.Plan(
            new BootstrapRequest("test"),
            [State("africa", source: true, border: true, levels: [2, 4])],
            new Dictionary<ArtifactKey, ArtifactRecord>(),
            [2, 4]
        );

        Keys(plan).Should().Equal("load");
    }

    [Fact]
    public void Plan_extracts_when_the_artifact_does_not_match_the_file()
    {
        var state = State("africa", source: true, border: true, levels: [2, 4]);
        var artifact = Artifact(state, size: state.BordersSize + 1, levels: [2, 4]);

        var plan = BootstrapPlanner.Plan(
            new BootstrapRequest("test"),
            [state],
            new Dictionary<ArtifactKey, ArtifactRecord> { [artifact.Key] = artifact },
            [2, 4]
        );

        Keys(plan).Should().Equal("load", "extract:africa", "load:refresh");
    }

    [Fact]
    public void Plan_extract_scope_forces_every_region()
    {
        var plan = BootstrapPlanner.Plan(
            new BootstrapRequest("test", RebuildScope.Extract),
            [State("africa", source: true, border: true, levels: [2, 4])],
            new Dictionary<ArtifactKey, ArtifactRecord>(),
            [2, 4]
        );

        Keys(plan).Should().Equal("load", "extract:africa", "load:refresh");
    }

    [Fact]
    public void Plan_geometry_cache_scope_clears_then_loads()
    {
        var plan = BootstrapPlanner.Plan(
            new BootstrapRequest("test", RebuildScope.GeometryCache),
            [State("africa", source: true, border: true, levels: [2, 4])],
            new Dictionary<ArtifactKey, ArtifactRecord>(),
            [2, 4]
        );

        Keys(plan).Should().Equal("cache:clear", "load");
    }

    [Fact]
    public void Plan_region_index_scope_loads_without_clearing_the_cache()
    {
        var plan = BootstrapPlanner.Plan(
            new BootstrapRequest("test", RebuildScope.RegionIndex),
            [State("africa", source: true, border: true, levels: [2, 4])],
            new Dictionary<ArtifactKey, ArtifactRecord>(),
            [2, 4]
        );

        Keys(plan).Should().Equal("load");
    }

    [Fact]
    public void Plan_pull_scope_checks_and_extracts_every_region()
    {
        var plan = BootstrapPlanner.Plan(
            new BootstrapRequest("test", RebuildScope.Pull),
            [State("africa", source: true, border: true, levels: [2, 4])],
            new Dictionary<ArtifactKey, ArtifactRecord>(),
            [2, 4]
        );

        Keys(plan).Should().Equal("load", "pull:africa", "extract:africa", "load:refresh");
    }

    [Fact]
    public void Plan_force_pulls_every_region()
    {
        var plan = BootstrapPlanner.Plan(
            new BootstrapRequest("test", RebuildScope.All, Force: true),
            [State("africa", source: true, border: true, levels: [2, 4])],
            new Dictionary<ArtifactKey, ArtifactRecord>(),
            [2, 4]
        );

        Keys(plan).Should().Equal("load", "pull:africa", "extract:africa", "load:refresh");
    }

    [Fact]
    public void Plan_filters_to_one_region()
    {
        var plan = BootstrapPlanner.Plan(
            new BootstrapRequest("test", Region: "asia"),
            [
                State("africa", source: false, border: false),
                State("asia", source: false, border: false),
            ],
            new Dictionary<ArtifactKey, ArtifactRecord>(),
            [2, 4]
        );

        Keys(plan).Should().Equal("load", "pull:asia", "extract:asia", "load:refresh");
    }

    [Fact]
    public void IsBordersCurrent_accepts_a_matching_artifact()
    {
        var state = State("africa", source: true, border: true, levels: [2, 4]);
        var artifact = Artifact(state, state.BordersSize, [2, 4]);

        BootstrapPlanner.IsBordersCurrent(state, artifact, [2, 4]).Should().BeTrue();
    }

    [Fact]
    public void IsBordersCurrent_rejects_an_older_border_file()
    {
        var state = State("africa", source: true, border: true, levels: [2, 4]) with
        {
            BordersModified = DateTimeOffset.UnixEpoch,
        };

        BootstrapPlanner.IsBordersCurrent(state, null, [2, 4]).Should().BeFalse();
    }

    [Fact]
    public void IsBordersCurrent_rejects_a_missing_sidecar_without_an_artifact()
    {
        var state = State("africa", source: true, border: true, levels: [2, 4]) with
        {
            SidecarReadable = false,
        };

        BootstrapPlanner.IsBordersCurrent(state, null, [2, 4]).Should().BeFalse();
    }

    [Fact]
    public void Scan_reads_the_source_border_and_sidecar_files()
    {
        var root = Directory.CreateTempSubdirectory("geotools-scan-");
        try
        {
            var borders = Directory
                .CreateDirectory(Path.Combine(root.FullName, "borders"))
                .FullName;
            File.WriteAllText(Path.Combine(root.FullName, "africa-latest.osm.pbf"), "pbf");
            File.WriteAllText(Path.Combine(borders, "africa-boundaries.geojsonseq"), "borders");
            File.WriteAllText(Path.Combine(borders, "africa-boundaries.levels"), "2,4");

            var options = new GeodataOptions
            {
                RootPath = root.FullName,
                BordersSubdirectory = "borders",
                Regions = ["africa"],
            };

            var state = BootstrapPlanner.Scan(options).Should().ContainSingle().Subject;

            state.SourceExists.Should().BeTrue();
            state.BordersExists.Should().BeTrue();
            state.SidecarReadable.Should().BeTrue();
            state.SidecarLevels.Should().Equal(2, 4);
            state.BordersModified.Should().BeOnOrAfter(state.SourceModified);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static string[] Keys(BootstrapPlan plan) =>
        plan.Steps.Select(step => step.Key.Value).ToArray();

    private static RegionState State(string region, bool source, bool border, int[]? levels = null)
    {
        var sourceTime = DateTimeOffset.UnixEpoch.AddDays(2);
        var borderTime = DateTimeOffset.UnixEpoch.AddDays(3);
        return new RegionState(
            region,
            $"/tmp/{region}-latest.osm.pbf",
            source,
            source ? 100 : 0,
            source ? sourceTime : DateTimeOffset.UnixEpoch,
            $"/tmp/{region}-boundaries.geojsonseq",
            $"/tmp/{region}-boundaries.levels",
            border,
            border ? 50 : 0,
            border ? borderTime : DateTimeOffset.UnixEpoch,
            levels ?? [],
            levels is not null
        );
    }

    private static ArtifactRecord Artifact(RegionState state, long size, int[] levels) =>
        new(
            ArtifactKeys.Borders(state.Region),
            ArtifactKind.Borders,
            state.Region,
            state.BordersPath,
            size,
            state.BordersModified,
            string.Join(',', levels),
            levels,
            null,
            ArtifactStatus.Current,
            state.BordersModified
        );
}
