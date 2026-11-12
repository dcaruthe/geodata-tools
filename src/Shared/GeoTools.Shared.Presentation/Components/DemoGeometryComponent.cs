using GeoTools.Shared.Application.Scenes;
using GeoTools.Shared.Domain.Primatives;
using GeoTools.Shared.Presentation.Views;

namespace GeoTools.Shared.Presentation.Components;

internal sealed class DemoGeometryComponent : IViewComponent
{
    public void Compose(ViewComposition composition)
    {
        var (width, height) = (composition.Size.Width, composition.Size.Height);
        Point At(double x, double y) => new((float)(x * width), (float)(y * height));
        var radius = (float)(0.0667 * width);

        composition.Add(
            new PolygonDto(
                [At(0.05, 0.075), At(0.25, 0.075), At(0.25, 0.275), At(0.05, 0.275)],
                new Color(0, 90, 200),
                1
            )
        );
        composition.Add(
            new PolygonDto(
                [At(0.3167, 0.075), At(0.5, 0.125), At(0.4667, 0.3), At(0.3, 0.25)],
                Color.White,
                6,
                Filled: false
            )
        );
        composition.Add(
            new PolygonDto(
                [At(0.6333, 0.075), At(0.75, 0.3), At(0.5667, 0.3)],
                new Color(0, 180, 90),
                1
            )
        );
        composition.Add(new LineDto(At(0.05, 0.875), At(0.95, 0.8), new Color(255, 220, 0), 5));
        composition.Add(new CircleDto(At(0.1667, 0.5625), radius, new Color(220, 40, 40), 1));
        composition.Add(new CircleDto(At(0.35, 0.5625), radius, new Color(40, 200, 40), 1));
        composition.Add(new CircleDto(At(0.5333, 0.5625), radius, new Color(40, 90, 240), 1));
        composition.Add(
            new CircleDto(At(0.7167, 0.5625), radius, new Color(240, 40, 200), 6, Filled: false)
        );
    }
}
