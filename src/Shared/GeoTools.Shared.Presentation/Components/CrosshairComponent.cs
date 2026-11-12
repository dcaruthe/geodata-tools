using GeoTools.Shared.Application.Scenes;
using GeoTools.Shared.Domain.Primatives;
using GeoTools.Shared.Presentation.Views;

namespace GeoTools.Shared.Presentation.Components;

internal sealed class CrosshairComponent : IViewComponent
{
    private static readonly Color Color = new(255, 255, 255, 120);
    private const float Arm = 24;
    private const float Radius = 3;

    public void Compose(ViewComposition composition)
    {
        var center = new Point(composition.Size.Width / 2f, composition.Size.Height / 2f);

        composition.Add(
            new LineDto(
                new Point(center.X - Arm, center.Y),
                new Point(center.X + Arm, center.Y),
                Color,
                1
            )
        );
        composition.Add(
            new LineDto(
                new Point(center.X, center.Y - Arm),
                new Point(center.X, center.Y + Arm),
                Color,
                1
            )
        );
        composition.Add(new CircleDto(center, Radius, Color, 1, Filled: false));
    }
}
