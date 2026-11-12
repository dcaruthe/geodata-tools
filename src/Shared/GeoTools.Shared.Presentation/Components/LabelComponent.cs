using GeoTools.Shared.Application.Scenes;
using GeoTools.Shared.Domain.Primatives;
using GeoTools.Shared.Presentation.Views;

namespace GeoTools.Shared.Presentation.Components;

internal sealed class LabelComponent(
    string text,
    float size,
    Color color,
    Func<ViewSize, Point> position
) : IViewComponent
{
    public void Compose(ViewComposition composition) =>
        composition.Add(new TextDto(position(composition.Size), text, size, color));
}
