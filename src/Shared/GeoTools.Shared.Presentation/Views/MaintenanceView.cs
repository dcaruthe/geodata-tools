using GeoTools.Shared.Application.Scenes;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Application.Views;
using GeoTools.Shared.Domain.Primatives;

namespace GeoTools.Shared.Presentation.Views;

internal sealed class MaintenanceView(MaintenanceController controller) : IView, IViewInputTarget
{
    private static readonly Color Background = new(18, 18, 22);
    private static readonly Color TitleColor = Color.White;
    private static readonly Color SectionColor = new(120, 150, 200);
    private static readonly Color TextColor = new(205, 210, 220);
    private static readonly Color MutedColor = new(130, 134, 142);
    private static readonly Color DangerColor = new(230, 120, 120);
    private static readonly Color SelectionColor = new(40, 52, 78);
    private static readonly Color StatusColor = new(120, 200, 150);
    private static readonly Color WarningColor = new(235, 180, 90);

    private const float RowHeight = 20;
    private const float Top = 140;

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
        composition.Add(
            new TextDto(
                new Point(48, 100),
                "Up/Down select   Left/Right change   Enter run   Backspace cancel",
                13,
                MutedColor
            )
        );

        var maxVisible = Math.Max(6, (int)((size.Height - Top - 80) / RowHeight));
        var (start, count) = MaintenanceLayout.Window(
            snapshot.Rows.Count,
            snapshot.SelectedIndex,
            maxVisible
        );
        var valueX = size.Width >= 760 ? Math.Min(470f, size.Width * 0.44f) : 0f;

        for (var index = 0; index < count; index++)
        {
            var rowIndex = start + index;
            var row = snapshot.Rows[rowIndex];
            var y = Top + index * RowHeight;

            if (rowIndex == snapshot.SelectedIndex)
                composition.Add(
                    new PolygonDto(
                        [
                            new Point(32, y - 4),
                            new Point(size.Width - 32, y - 4),
                            new Point(size.Width - 32, y + RowHeight - 6),
                            new Point(32, y + RowHeight - 6),
                        ],
                        SelectionColor,
                        1,
                        Filled: true
                    )
                );

            var color = RowColor(row);
            var label = row.Destructive ? $"! {row.Label}" : row.Label;

            if (
                row.Kind == MaintenanceRowKind.Info
                && !row.Selectable
                && string.IsNullOrEmpty(row.Value)
            )
            {
                composition.Add(new TextDto(new Point(48, y), label, 14, SectionColor));
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

        if (start > 0)
            composition.Add(
                new TextDto(new Point(size.Width - 90, Top - 18), "more above", 11, MutedColor)
            );
        if (start + count < snapshot.Rows.Count)
            composition.Add(
                new TextDto(
                    new Point(size.Width - 90, Top + count * RowHeight + 2),
                    "more below",
                    11,
                    MutedColor
                )
            );

        if (snapshot.PendingConfirmation is not null)
            composition.Add(
                new TextDto(
                    new Point(48, size.Height - 92),
                    "Enter again to confirm. Any other key cancels.",
                    14,
                    WarningColor
                )
            );

        if (!string.IsNullOrWhiteSpace(snapshot.Status))
            composition.Add(
                new TextDto(new Point(48, size.Height - 68), snapshot.Status, 14, StatusColor)
            );

        composition.Add(
            new TextDto(
                new Point(48, size.Height - 40),
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
