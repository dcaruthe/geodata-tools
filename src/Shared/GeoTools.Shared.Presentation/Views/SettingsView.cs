using GeoTools.Shared.Application.Scenes;
using GeoTools.Shared.Application.Views;
using GeoTools.Shared.Domain.Primatives;
using GeoTools.Shared.Presentation.Components;
using GeoTools.Shared.Presentation.Options;
using Microsoft.Extensions.Options;

namespace GeoTools.Shared.Presentation.Views;

internal sealed class SettingsView : IView
{
    private readonly IViewComponent[] _components;

    public SettingsView(IOptions<WindowOptions> options)
    {
        var window = options.Value;
        _components =
        [
            new FrameComponent(),
            new AspectRatioComponent(window.Width, window.Height),
            new SwatchRowComponent(),
            new LabelComponent(window.Title, 28, Color.White, _ => new Point(48, 96)),
            new LabelComponent(
                $"{window.Width} x {window.Height}",
                18,
                new Color(180, 180, 180),
                _ => new Point(48, 132)
            ),
            new LabelComponent(ViewText.KeyHint, 14, new Color(150, 150, 150), size => new Point(
                48,
                size.Height - 90
            )),
        ];
    }

    public ViewId Id => ViewId.Settings;

    public SceneDto Compose(ViewSize size)
    {
        var composition = new ViewComposition(size, new Color(24, 24, 28));
        foreach (var component in _components)
            component.Compose(composition);
        return composition.Build();
    }
}
