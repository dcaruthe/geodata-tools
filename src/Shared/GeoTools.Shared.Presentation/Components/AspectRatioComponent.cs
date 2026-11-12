using GeoTools.Shared.Application.Scenes;
using GeoTools.Shared.Domain.Primatives;
using GeoTools.Shared.Presentation.Views;

namespace GeoTools.Shared.Presentation.Components;

internal sealed class AspectRatioComponent(int width, int height) : IViewComponent
{
    private static readonly Color AspectColor = new(90, 120, 200);
    private const float MaxViewportFraction = 0.6f;

    public void Compose(ViewComposition composition)
    {
        var size = composition.Size;
        var scale = Math.Min(
            MaxViewportFraction * size.Width / width,
            MaxViewportFraction * size.Height / height
        );
        var rectWidth = width * scale;
        var rectHeight = height * scale;
        var left = (size.Width - rectWidth) / 2f;
        var top = (size.Height - rectHeight) / 2f;

        composition.Add(
            new PolygonDto(
                [
                    new Point(left, top),
                    new Point(left + rectWidth, top),
                    new Point(left + rectWidth, top + rectHeight),
                    new Point(left, top + rectHeight),
                ],
                AspectColor,
                2,
                Filled: false
            )
        );
    }
}
