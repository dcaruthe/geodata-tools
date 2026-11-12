using GeoTools.Shared.Application.Scenes;
using GeoTools.Shared.Domain.Primatives;
using GeoTools.Shared.Presentation.Views;

namespace GeoTools.Shared.Presentation.Components;

internal sealed class GridComponent : IViewComponent
{
    private static readonly Color GridColor = new(40, 40, 40);
    private const int Spacing = 100;

    public void Compose(ViewComposition composition)
    {
        var size = composition.Size;

        for (var x = 0; x <= size.Width; x += Spacing)
            composition.Add(
                new LineDto(new Point(x, 0), new Point(x, size.Height), GridColor, Thickness: 1)
            );

        for (var y = 0; y <= size.Height; y += Spacing)
            composition.Add(
                new LineDto(new Point(0, y), new Point(size.Width, y), GridColor, Thickness: 1)
            );
    }
}
