using GeoTools.Shared.Application.Scenes;
using GeoTools.Shared.Domain.Primatives;
using GeoTools.Shared.Presentation.Views;

namespace GeoTools.Shared.Presentation.Components;

internal sealed class FrameComponent : IViewComponent
{
    private static readonly Color FrameColor = new(70, 70, 80);
    private const float Inset = 24;

    public void Compose(ViewComposition composition)
    {
        var size = composition.Size;
        composition.Add(
            new PolygonDto(
                [
                    new Point(Inset, Inset),
                    new Point(size.Width - Inset, Inset),
                    new Point(size.Width - Inset, size.Height - Inset),
                    new Point(Inset, size.Height - Inset),
                ],
                FrameColor,
                2,
                Filled: false
            )
        );
    }
}
