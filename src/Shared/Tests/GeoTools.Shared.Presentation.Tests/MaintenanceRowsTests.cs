using FluentAssertions;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Domain.Geospatial;
using GeoTools.Shared.Presentation.Views;

namespace GeoTools.Shared.Presentation.Tests;

public sealed class MaintenanceRowsTests
{
    [Fact]
    public void Build_includes_state_settings_flags_data_and_actions()
    {
        var rows = MaintenanceRows.Build(Inputs());

        var ids = rows.Select(row => row.Id).ToArray();
        ids.Should()
            .Contain([
                "status",
                "section:window",
                "windowSize",
                "displayLevels",
                "tolerance",
                "extractLevels",
                "flag:MapGrid",
                "flag:MapCrosshair",
                "flag:GeodataAutoPull",
                "artifact:africa",
                "section:index",
                "regionIndex",
                "action:check",
                "action:repull",
                "action:extract",
                "action:cache",
                "action:reindex",
                "action:cancel",
                "action:retry",
                "action:reset",
            ]);
    }

    [Fact]
    public void Build_disables_rebuilds_while_running_and_enables_cancel()
    {
        var rows = MaintenanceRows.Build(Inputs(running: true));

        rows.Single(row => row.Id == "action:repull").Enabled.Should().BeFalse();
        rows.Single(row => row.Id == "action:cancel").Enabled.Should().BeTrue();
        rows.Should().Contain(row => row.Id == "progress");
    }

    [Fact]
    public void Build_includes_the_window_section_and_size()
    {
        var rows = MaintenanceRows.Build(Inputs());

        rows.Should()
            .Contain(row => row.Id == "section:window" && row.Kind == MaintenanceRowKind.Info);
        rows.Should()
            .Contain(row =>
                row.Id == "windowSize"
                && row.Label == "Window size"
                && row.Value == "1600x900"
                && row.Kind == MaintenanceRowKind.Choice
            );
    }

    [Fact]
    public void Move_skips_rows_that_are_not_selectable()
    {
        var rows = MaintenanceRows.Build(Inputs());

        var moved = MaintenanceRows.Move(rows, 0, 1);

        rows[moved].Selectable.Should().BeTrue();
        rows[moved].Id.Should().Be("displayLevels");
    }

    [Fact]
    public void IndexOf_falls_back_to_the_first_selectable_row()
    {
        var rows = MaintenanceRows.Build(Inputs());

        MaintenanceRows.IndexOf(rows, "missing").Should().Be(MaintenanceRows.FirstSelectable(rows));
    }

    [Fact]
    public void Window_keeps_the_selection_visible()
    {
        MaintenanceLayout.Window(25, 0, 10).Should().Be((0, 10));
        MaintenanceLayout.Window(25, 24, 10).Should().Be((15, 10));
        MaintenanceLayout.Window(5, 2, 10).Should().Be((0, 5));
        MaintenanceLayout.Window(0, 0, 10).Should().Be((0, 0));
    }

    [Fact]
    public void RowsThatFit_reserves_the_footer_status_and_prompt()
    {
        var footer = MaintenanceLayout.RowsThatFit(800, hasStatus: false, hasPrompt: false);
        var status = MaintenanceLayout.RowsThatFit(800, hasStatus: true, hasPrompt: false);
        var prompt = MaintenanceLayout.RowsThatFit(800, hasStatus: true, hasPrompt: true);

        footer.Should().BeGreaterThanOrEqualTo(29);
        status.Should().BeLessThan(footer);
        prompt.Should().BeLessThan(status);
        MaintenanceLayout.RowsThatFit(260, hasStatus: true, hasPrompt: true).Should().Be(1);
    }

    [Theory]
    [InlineData(260f, false, false)]
    [InlineData(260f, true, true)]
    [InlineData(300f, false, false)]
    [InlineData(400f, true, false)]
    [InlineData(800f, false, false)]
    public void RowsThatFit_keeps_the_last_row_above_the_bottom_block(
        float height,
        bool hasStatus,
        bool hasPrompt
    )
    {
        var count = MaintenanceLayout.RowsThatFit(height, hasStatus, hasPrompt);
        var lastInk = MaintenanceLayout.Baseline(count - 1) + MaintenanceLayout.TextDescent;
        var blockTop = MaintenanceLayout.BottomBlockTop(height, hasStatus, hasPrompt);

        lastInk.Should().BeLessThanOrEqualTo(blockTop - MaintenanceLayout.BlockGap);
    }

    [Fact]
    public void FormatLevels_marks_an_empty_selection()
    {
        MaintenanceRows.FormatLevels([]).Should().Be("none");
        MaintenanceRows.FormatLevels([2, 4]).Should().Be("2,4");
    }

    [Fact]
    public void Build_includes_the_last_run_and_the_extracted_levels()
    {
        var inputs = Inputs() with
        {
            LatestRun = new RunRecord(
                RunId.New(),
                "test",
                RunStatus.Succeeded,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch,
                null
            ),
        };

        var rows = MaintenanceRows.Build(inputs);

        rows.Should().Contain(row => row.Id == "lastRun" && row.Value.Contains("Succeeded"));
        rows.Should().Contain(row => row.Id == "availableLevels" && row.Value == "2,4");
        rows.Single(row => row.Id == "flag:MapCrosshair").Value.Should().Be("off");
    }

    [Theory]
    [InlineData(BootstrapPhase.Succeeded, "Ready")]
    [InlineData(BootstrapPhase.Cancelled, "Cancelled")]
    [InlineData(BootstrapPhase.Failed, "Failed")]
    [InlineData(BootstrapPhase.Idle, "Idle")]
    public void Build_describes_each_phase(BootstrapPhase phase, string expected)
    {
        var inputs = Inputs() with
        {
            Progress = BootstrapProgressSnapshot.Idle with { Phase = phase, Error = "boom" },
        };

        var rows = MaintenanceRows.Build(inputs);

        rows.Single(row => row.Id == "status").Value.Should().StartWith(expected);
    }

    [Fact]
    public void Move_handles_empty_and_non_selectable_rows()
    {
        MaintenanceRows.Move([], 0, 1).Should().Be(0);

        var rows = new[]
        {
            new MaintenanceRow("a", "A", "", MaintenanceRowKind.Info, Selectable: false),
            new MaintenanceRow("b", "B", "", MaintenanceRowKind.Info, Selectable: false),
        };

        MaintenanceRows.Move(rows, 0, 1).Should().Be(0);
    }

    [Fact]
    public void LastSelectable_skips_the_trailing_non_selectable_rows()
    {
        var rows = MaintenanceRows.Build(Inputs());

        rows[MaintenanceRows.LastSelectable(rows)].Id.Should().Be("action:reset");
        MaintenanceRows.LastSelectable([]).Should().Be(0);
    }

    [Fact]
    public void Move_wraps_from_the_last_row()
    {
        var rows = MaintenanceRows.Build(Inputs());
        var last = rows.Count - 1;
        while (!rows[last].Selectable)
            last--;

        var moved = MaintenanceRows.Move(rows, last, 1);

        rows[moved].Should().Be(rows[MaintenanceRows.FirstSelectable(rows)]);
    }

    [Fact]
    public void Build_reports_the_region_index_stats()
    {
        var inputs = Inputs() with
        {
            Index = new RegionIndexStats(
                5449,
                113345,
                [2, 4],
                new GeoBounds(-180, -90, 180, 90),
                12_345_678
            ),
        };

        var rows = MaintenanceRows.Build(inputs);

        rows.Single(row => row.Id == "regionIndex")
            .Value.Should()
            .Be("5449 regions, 113345 lines, levels 2,4, 11.8 MB");
    }

    [Fact]
    public void Build_marks_the_region_index_unavailable_when_it_is_missing()
    {
        var rows = MaintenanceRows.Build(Inputs());

        rows.Single(row => row.Id == "regionIndex").Value.Should().Be("unavailable");
    }

    [Fact]
    public void Build_disables_reindex_while_running()
    {
        var rows = MaintenanceRows.Build(Inputs(running: true));

        rows.Single(row => row.Id == "action:reindex").Enabled.Should().BeFalse();
    }

    internal static MaintenanceInputs Inputs(bool running = false) =>
        new(
            running
                ? BootstrapProgressSnapshot.Idle with
                {
                    Phase = BootstrapPhase.Running,
                    Label = "Extracting africa",
                    Fraction = 0.3,
                }
                : BootstrapProgressSnapshot.Idle,
            null,
            new MapSettings([2]),
            new GeodataSettings([2, 4], 0.002),
            new FixedWindow(1600, 900),
            new Dictionary<FeatureFlag, bool>
            {
                [FeatureFlag.MapGrid] = true,
                [FeatureFlag.MapCrosshair] = false,
                [FeatureFlag.GeodataAutoPull] = true,
            },
            [2, 4],
            [new MaintenanceArtifactSummary("africa", "7.5 GB", "174 MB")],
            running
        );
}
