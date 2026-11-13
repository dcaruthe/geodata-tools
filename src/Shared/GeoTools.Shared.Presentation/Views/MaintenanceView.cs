using GeoTools.Shared.Application.Scenes;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Application.Views;
using GeoTools.Shared.Domain.Primatives;

namespace GeoTools.Shared.Presentation.Views;

internal sealed class MaintenanceView(MaintenanceController controller) : IView, IViewInputTarget
{
    private static readonly Color Background = new(18, 18, 22);
    private static readonly Color TitleColor = Color.White;
    private static readonly Color SectionColor = new(205, 210, 220);
    private static readonly Color TextColor = new(205, 210, 220);
    private static readonly Color MutedColor = new(130, 134, 142);
    private static readonly Color DangerColor = new(230, 120, 120);
    private static readonly Color SelectionColor = new(40, 52, 78);
    private static readonly Color StatusColor = new(120, 200, 150);
    private static readonly Color WarningColor = new(235, 180, 90);
    private static readonly Color ScrollbarTrackColor = new(36, 40, 50);
    private static readonly Color ScrollbarThumbColor = new(78, 92, 126);

    private const string KeysHint = "Up/Down select   Left/Right change";
    private const string ActionsHint = "Enter run   Backspace cancel   Home/End jump";
    private const float HintBaseline = 100;
    private const float HintLineGap = 18;
    private const float HintSingleLineWidth = 620;
    private const float ScrollbarWidth = 4;
    private const float ScrollbarRightInset = 26;

    private SceneDto? _cached;
    private ViewSize _cachedSize;
    private long _cachedVersion;

    public ViewId Id => ViewId.Maintenance;

    public bool Handle(ViewKey key) => controller.Handle(key);

    public SceneDto Compose(ViewSize size)
    {
        var snapshot = controller.Snapshot;
        if (_cached is not null && size == _cachedSize && snapshot.Version == _cachedVersion)
            return _cached;

        var composition = new ViewComposition(size, Background);
        composition.Add(new TextDto(new Point(48, 64), "Maintenance", 26, TitleColor));
        ComposeHint(composition, size);

        var hasStatus = !string.IsNullOrWhiteSpace(snapshot.Status);
        var hasPrompt = snapshot.PendingConfirmation is not null;
        var (start, count) = MaintenanceLayout.Window(
            snapshot.Rows.Count,
            snapshot.SelectedIndex,
            MaintenanceLayout.RowsThatFit(size.Height, hasStatus, hasPrompt)
        );
        var valueX = size.Width >= 760 ? Math.Min(470f, size.Width * 0.44f) : 0f;

        for (var index = 0; index < count; index++)
        {
            var rowIndex = start + index;
            var row = snapshot.Rows[rowIndex];
            var y = MaintenanceLayout.Baseline(index);

            if (rowIndex == snapshot.SelectedIndex)
            {
                var (barTop, barHeight) = MaintenanceLayout.Selection(index);
                composition.Add(Rectangle(32, barTop, size.Width - 64, barHeight, SelectionColor));
            }

            var color = RowColor(row);
            var label = row.Destructive ? $"! {row.Label}" : row.Label;

            if (
                row.Kind == MaintenanceRowKind.Info
                && !row.Selectable
                && string.IsNullOrEmpty(row.Value)
            )
            {
                composition.Add(
                    new TextDto(new Point(48, y), label.ToUpperInvariant(), 12, SectionColor)
                );
                continue;
            }

            if (valueX > 0)
            {
                composition.Add(new TextDto(new Point(48, y), label, 14, color));
                if (!string.IsNullOrEmpty(row.Value))
                    composition.Add(new TextDto(new Point(valueX, y), row.Value, 14, color));
            }
            else
            {
                var text = string.IsNullOrEmpty(row.Value) ? label : $"{label}: {row.Value}";
                composition.Add(new TextDto(new Point(48, y), text, 14, color));
            }
        }

        ComposeScrollbar(composition, size, snapshot.Rows.Count, start, count);

        if (hasPrompt)
            composition.Add(
                new TextDto(
                    new Point(48, size.Height - MaintenanceLayout.PromptBaselineFromBottom),
                    "Enter again to confirm. Moving or leaving cancels.",
                    14,
                    WarningColor
                )
            );

        if (hasStatus)
            composition.Add(
                new TextDto(
                    new Point(48, size.Height - MaintenanceLayout.StatusBaselineFromBottom),
                    snapshot.Status,
                    14,
                    StatusColor
                )
            );

        composition.Add(
            new TextDto(
                new Point(48, size.Height - MaintenanceLayout.FooterBaselineFromBottom),
                "1: map   2: maintenance   Tab: view   Esc: close",
                13,
                MutedColor
            )
        );

        _cached = composition.Build();
        _cachedSize = size;
        _cachedVersion = snapshot.Version;
        return _cached;
    }

    private static void ComposeHint(ViewComposition composition, ViewSize size)
    {
        if (size.Width >= HintSingleLineWidth)
        {
            composition.Add(
                new TextDto(
                    new Point(48, HintBaseline),
                    $"{KeysHint}   {ActionsHint}",
                    13,
                    MutedColor
                )
            );
            return;
        }

        composition.Add(new TextDto(new Point(48, HintBaseline), KeysHint, 13, MutedColor));
        composition.Add(
            new TextDto(new Point(48, HintBaseline + HintLineGap), ActionsHint, 13, MutedColor)
        );
    }

    private static void ComposeScrollbar(
        ViewComposition composition,
        ViewSize size,
        int total,
        int start,
        int count
    )
    {
        if (total <= count)
            return;

        var (trackTop, _) = MaintenanceLayout.Selection(0);
        var trackHeight = count * MaintenanceLayout.RowHeight;
        var thumbHeight = Math.Clamp(
            trackHeight * count / total,
            Math.Min(16f, trackHeight),
            trackHeight
        );
        var thumbTop = trackTop + (trackHeight - thumbHeight) * start / Math.Max(1, total - count);
        var x = size.Width - ScrollbarRightInset;

        composition.Add(Rectangle(x, trackTop, ScrollbarWidth, trackHeight, ScrollbarTrackColor));
        composition.Add(Rectangle(x, thumbTop, ScrollbarWidth, thumbHeight, ScrollbarThumbColor));
    }

    private static PolygonDto Rectangle(float x, float y, float width, float height, Color color) =>
        new(
            [
                new Point(x, y),
                new Point(x + width, y),
                new Point(x + width, y + height),
                new Point(x, y + height),
            ],
            color,
            1,
            Filled: true
        );

    private static Color RowColor(MaintenanceRow row)
    {
        if (!row.Enabled)
            return MutedColor;
        if (row.Destructive)
            return DangerColor;
        if (row.Kind == MaintenanceRowKind.Action)
            return new Color(170, 200, 250);
        return TextColor;
    }
}
