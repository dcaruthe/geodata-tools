using GeoTools.Shared.Application.Scenes;
using GeoTools.Shared.Domain.Primatives;
using GeoTools.Shared.Presentation.Views;

namespace GeoTools.Shared.Presentation.Components;

internal static class SpinnerComponent
{
    private const int Segments = 12;

    public static void Compose(
        ViewComposition composition,
        Point center,
        float radius,
        double phaseSeconds
    )
    {
        var phase = phaseSeconds * 1.6;

        for (var index = 0; index < Segments; index++)
        {
            var angle = index * Math.Tau / Segments;
            var weight = (Math.Sin(phase - index * 0.5) + 1) / 2;
            var alpha = (byte)(25 + 205 * weight);
            var color = new Color(130, 180, 255, alpha);
            var start = new Point(
                center.X + (float)(Math.Cos(angle) * radius * 0.72),
                center.Y + (float)(Math.Sin(angle) * radius * 0.72)
            );
            var end = new Point(
                center.X + (float)(Math.Cos(angle) * radius),
                center.Y + (float)(Math.Sin(angle) * radius)
            );

            composition.Add(new LineDto(start, end, color, 3));
        }
    }
}
