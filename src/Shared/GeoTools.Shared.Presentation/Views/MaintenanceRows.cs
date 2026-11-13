using GeoTools.Shared.Application.State;

namespace GeoTools.Shared.Presentation.Views;

internal enum MaintenanceRowKind
{
    Info,
    Choice,
    Toggle,
    Action,
}

internal sealed record MaintenanceRow(
    string Id,
    string Label,
    string Value,
    MaintenanceRowKind Kind,
    bool Selectable = true,
    bool Enabled = true,
    bool Destructive = false
);

internal sealed record MaintenanceSnapshot(
    IReadOnlyList<MaintenanceRow> Rows,
    int SelectedIndex,
    string Status,
    string? PendingConfirmation,
    long Version
)
{
    public static MaintenanceSnapshot Empty { get; } = new([], 0, string.Empty, null, 0);

    public MaintenanceRow? Selected =>
        Rows.Count == 0 ? null : Rows[Math.Clamp(SelectedIndex, 0, Rows.Count - 1)];
}

internal sealed record MaintenanceArtifactSummary(string Region, string? Pbf, string? Borders);

internal sealed record MaintenanceInputs(
    BootstrapProgressSnapshot Progress,
    RunRecord? LatestRun,
    MapSettings Map,
    GeodataSettings Geodata,
    IReadOnlyDictionary<FeatureFlag, bool> Flags,
    IReadOnlyList<int> AvailableLevels,
    IReadOnlyList<MaintenanceArtifactSummary> Artifacts,
    bool IsRunning
);

internal static class MaintenanceRows
{
    public static IReadOnlyList<MaintenanceRow> Build(MaintenanceInputs inputs)
    {
        var rows = new List<MaintenanceRow>
        {
            new("status", "State", PhaseText(inputs.Progress), MaintenanceRowKind.Info, false),
        };

        if (inputs.Progress.Phase == BootstrapPhase.Running)
        {
            rows.Add(
                new(
                    "progress",
                    "Progress",
                    $"{inputs.Progress.Label} {inputs.Progress.Fraction:P0}",
                    MaintenanceRowKind.Info,
                    false
                )
            );
        }

        if (inputs.LatestRun is { } run)
        {
            rows.Add(
                new(
                    "lastRun",
                    "Last run",
                    $"{run.Status} at {run.StartedAt.ToLocalTime():HH:mm}",
                    MaintenanceRowKind.Info,
                    false
                )
            );
        }

        rows.Add(new("section:display", "Display", "", MaintenanceRowKind.Info, false));
        rows.Add(
            new(
                "availableLevels",
                "Extracted levels",
                FormatLevels(inputs.AvailableLevels),
                MaintenanceRowKind.Info,
                false
            )
        );
        rows.Add(
            new(
                "displayLevels",
                "Display levels",
                FormatLevels(inputs.Map.AdminLevels),
                MaintenanceRowKind.Choice
            )
        );
        rows.Add(
            new(
                "tolerance",
                "Simplify tolerance",
                inputs.Geodata.SimplifyToleranceDegrees.ToString("0.###"),
                MaintenanceRowKind.Choice
            )
        );

        rows.Add(new("section:extract", "Extraction", "", MaintenanceRowKind.Info, false));
        rows.Add(
            new(
                "extractLevels",
                "Extract levels",
                FormatLevels(inputs.Geodata.ExtractAdminLevels),
                MaintenanceRowKind.Choice
            )
        );

        rows.Add(new("section:flags", "Feature flags", "", MaintenanceRowKind.Info, false));
        rows.Add(
            new(
                $"flag:{FeatureFlag.MapGrid}",
                "Map grid",
                OnOff(inputs.Flags, FeatureFlag.MapGrid),
                MaintenanceRowKind.Toggle
            )
        );
        rows.Add(
            new(
                $"flag:{FeatureFlag.MapCrosshair}",
                "Crosshair",
                OnOff(inputs.Flags, FeatureFlag.MapCrosshair),
                MaintenanceRowKind.Toggle
            )
        );
        rows.Add(
            new(
                $"flag:{FeatureFlag.GeodataAutoPull}",
                "Auto download",
                OnOff(inputs.Flags, FeatureFlag.GeodataAutoPull),
                MaintenanceRowKind.Toggle
            )
        );

        rows.Add(new("section:data", "Data", "", MaintenanceRowKind.Info, false));
        foreach (var artifact in inputs.Artifacts)
        {
            rows.Add(
                new(
                    $"artifact:{artifact.Region}",
                    artifact.Region,
                    $"PBF {artifact.Pbf ?? "-"}   Borders {artifact.Borders ?? "-"}",
                    MaintenanceRowKind.Info
                )
            );
        }

        rows.Add(new("section:actions", "Actions", "", MaintenanceRowKind.Info, false));
        rows.Add(
            new(
                "action:check",
                "Check for updates",
                "",
                MaintenanceRowKind.Action,
                Enabled: !inputs.IsRunning
            )
        );
        rows.Add(
            new(
                "action:repull",
                "Force repull",
                "",
                MaintenanceRowKind.Action,
                Enabled: !inputs.IsRunning,
                Destructive: true
            )
        );
        rows.Add(
            new(
                "action:extract",
                "Rebuild borders",
                "",
                MaintenanceRowKind.Action,
                Enabled: !inputs.IsRunning,
                Destructive: true
            )
        );
        rows.Add(
            new(
                "action:cache",
                "Rebuild geometry cache",
                "",
                MaintenanceRowKind.Action,
                Enabled: !inputs.IsRunning
            )
        );
        rows.Add(
            new(
                "action:cancel",
                "Cancel running work",
                "",
                MaintenanceRowKind.Action,
                Enabled: inputs.IsRunning
            )
        );
        rows.Add(new("action:retry", "Retry failed events", "", MaintenanceRowKind.Action));
        rows.Add(
            new("action:reset", "Reset settings", "", MaintenanceRowKind.Action, Destructive: true)
        );

        return rows;
    }

    public static int FirstSelectable(IReadOnlyList<MaintenanceRow> rows)
    {
        for (var index = 0; index < rows.Count; index++)
        {
            if (rows[index].Selectable)
                return index;
        }

        return 0;
    }

    public static int LastSelectable(IReadOnlyList<MaintenanceRow> rows)
    {
        for (var index = rows.Count - 1; index >= 0; index--)
        {
            if (rows[index].Selectable)
                return index;
        }

        return 0;
    }

    public static int IndexOf(IReadOnlyList<MaintenanceRow> rows, string id)
    {
        for (var index = 0; index < rows.Count; index++)
        {
            if (rows[index].Id == id)
                return index;
        }

        return FirstSelectable(rows);
    }

    public static int Move(IReadOnlyList<MaintenanceRow> rows, int selected, int direction)
    {
        if (rows.Count == 0)
            return 0;

        var index = selected;
        for (var attempt = 0; attempt < rows.Count; attempt++)
        {
            index = (index + direction + rows.Count) % rows.Count;
            if (rows[index].Selectable)
                return index;
        }

        return selected;
    }

    public static string FormatLevels(IReadOnlyList<int> levels) =>
        levels.Count == 0 ? "none" : string.Join(',', levels);

    private static string OnOff(IReadOnlyDictionary<FeatureFlag, bool> flags, FeatureFlag flag) =>
        flags.TryGetValue(flag, out var enabled) && enabled ? "on" : "off";

    private static string PhaseText(BootstrapProgressSnapshot progress) =>
        progress.Phase switch
        {
            BootstrapPhase.Running => $"Running: {progress.Label}",
            BootstrapPhase.Succeeded => "Ready",
            BootstrapPhase.Failed => $"Failed: {progress.Error ?? "see the log"}",
            BootstrapPhase.Cancelled => "Cancelled",
            _ => "Idle",
        };
}

internal static class MaintenanceLayout
{
    // Row geometry. The view draws text on the baseline, so the bar is the glyph box plus a
    // padding that keeps it clear of the row above and below.
    public const float Top = 140;
    public const float RowHeight = 20;
    public const float TextAscent = 11;
    public const float TextDescent = 3;
    public const float RowPadding = 3;
    public const float BlockGap = 6;

    // Baseline offsets from the window bottom for the footer, status, and confirmation blocks.
    public const float FooterBaselineFromBottom = 40;
    public const float StatusBaselineFromBottom = 68;
    public const float PromptBaselineFromBottom = 92;

    public static float Baseline(int visibleRow) => Top + visibleRow * RowHeight;

    public static (float Top, float Height) Selection(int visibleRow) =>
        (Baseline(visibleRow) - TextAscent - RowPadding, TextAscent + TextDescent + RowPadding * 2);

    public static int RowsThatFit(float height, bool hasStatus, bool hasPrompt)
    {
        var blockTop = BottomBlockTop(height, hasStatus, hasPrompt);
        return Math.Max(1, (int)Math.Floor((blockTop - BlockGap - Top) / RowHeight) + 1);
    }

    public static float BottomBlockTop(float height, bool hasStatus, bool hasPrompt)
    {
        var baseline =
            hasPrompt ? PromptBaselineFromBottom
            : hasStatus ? StatusBaselineFromBottom
            : FooterBaselineFromBottom;
        return height - baseline - TextAscent;
    }

    public static (int Start, int Count) Window(int total, int selected, int maxVisible)
    {
        if (total <= 0)
            return (0, 0);

        var visible = Math.Max(1, Math.Min(total, maxVisible));
        if (visible == total)
            return (0, total);

        var start = Math.Clamp(selected - visible / 2, 0, total - visible);
        return (start, visible);
    }
}
