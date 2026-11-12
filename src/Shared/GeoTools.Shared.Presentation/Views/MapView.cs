using GeoTools.Shared.Application.Scenes;
using GeoTools.Shared.Application.Views;
using GeoTools.Shared.Domain.Primatives;
using GeoTools.Shared.Presentation.Components;

namespace GeoTools.Shared.Presentation.Views;

internal sealed class MapView : IView
{
    private static readonly IViewComponent[] Components =
    [
        new GridComponent(),
        new DemoGeometryComponent(),
        new CrosshairComponent(),
        new LabelComponent(ViewText.KeyHint, 14, new Color(150, 150, 150), size => new Point(
            16,
            size.Height - 16
        )),
    ];

    public ViewId Id => ViewId.Map;

    public SceneDto Compose(ViewSize size)
    {
        var composition = new ViewComposition(size, Color.Black);
        foreach (var component in Components)
            component.Compose(composition);
        return composition.Build();
    }
}
