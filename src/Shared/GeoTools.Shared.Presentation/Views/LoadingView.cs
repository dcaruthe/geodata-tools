using GeoTools.Shared.Application.Scenes;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Application.Views;
using GeoTools.Shared.Domain.Primatives;
using GeoTools.Shared.Presentation.Components;

namespace GeoTools.Shared.Presentation.Views;

internal sealed class LoadingView(IProgressStore progress, TimeProvider clock) : IView
{
    private static readonly Color TitleColor = Color.White;
    private static readonly Color TextColor = new(180, 186, 198);
    private static readonly Color ErrorColor = new(235, 120, 120);
    private static readonly Color HintColor = new(140, 140, 150);
    private readonly DateTimeOffset _started = clock.GetUtcNow();

    public ViewId Id => ViewId.Loading;

    public SceneDto Compose(ViewSize size)
    {
        var snapshot = progress.Current;
        var composition = new ViewComposition(size, new Color(12, 14, 20));

        var centerX = size.Width / 2f;
        var top = Math.Max(48f, size.Height * 0.2f);

        composition.Add(
            new TextDto(new Point(centerX - 90, top), "Preparing geodata", 26, TitleColor)
        );

        SpinnerComponent.Compose(
            composition,
            new Point(centerX, top + 96),
            26,
            (clock.GetUtcNow() - _started).TotalSeconds
        );

        var label = string.IsNullOrWhiteSpace(snapshot.Label) ? "Starting" : snapshot.Label;
        composition.Add(new TextDto(new Point(centerX - 150, top + 156), label, 16, TextColor));

        var barWidth = Math.Min(size.Width - 160f, 520f);
        ProgressBarComponent.Compose(
            composition,
            centerX - barWidth / 2,
            top + 186,
            barWidth,
            14,
            snapshot.Fraction
        );

        composition.Add(
            new TextDto(
                new Point(centerX - 40, top + 212),
                $"{snapshot.Fraction:P0}",
                14,
                TextColor
            )
        );

        if (!string.IsNullOrWhiteSpace(snapshot.Detail))
            composition.Add(
                new TextDto(new Point(centerX - 110, top + 236), snapshot.Detail, 13, HintColor)
            );

        if (snapshot.Progress.EtaSeconds is { } eta && eta > 0)
            composition.Add(
                new TextDto(
                    new Point(centerX - 90, top + 258),
                    $"About {FormatDuration(eta)} remaining",
                    13,
                    HintColor
                )
            );

        if (!string.IsNullOrWhiteSpace(snapshot.Error))
            composition.Add(
                new TextDto(new Point(48, size.Height - 96), snapshot.Error, 14, ErrorColor)
            );

        composition.Add(
            new TextDto(
                new Point(48, size.Height - 64),
                "2: maintenance   Esc: close",
                14,
                HintColor
            )
        );

        return composition.Build();
    }

    private static string FormatDuration(double seconds)
    {
        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1
            ? $"{span.Hours}h {span.Minutes}m"
            : $"{Math.Max(1, span.Minutes)}m {span.Seconds}s";
    }
}
