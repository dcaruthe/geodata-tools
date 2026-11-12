using GeoTools.Shared.Application.Scenes;
using GeoTools.Shared.Domain.Primatives;
using GeoTools.Shared.Presentation.Views;

namespace GeoTools.Shared.Presentation.Components;

internal static class ProgressBarComponent
{
    private static readonly Color TrackColor = new(38, 42, 52);
    private static readonly Color FillColor = new(80, 150, 240);

    public static void Compose(
        ViewComposition composition,
        float left,
        float top,
        float width,
        float height,
        double fraction
    )
    {
        composition.Add(
            new PolygonDto(
                [
                    new Point(left, top),
                    new Point(left + width, top),
                    new Point(left + width, top + height),
                    new Point(left, top + height),
                ],
                TrackColor,
                1,
                Filled: true
            )
        );

        var filled = (float)(width * Math.Clamp(fraction, 0, 1));
        if (filled <= 0)
            return;

        composition.Add(
            new PolygonDto(
                [
                    new Point(left, top),
                    new Point(left + filled, top),
                    new Point(left + filled, top + height),
                    new Point(left, top + height),
                ],
                FillColor,
                1,
                Filled: true
            )
        );
    }
}
