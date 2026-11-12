using GeoTools.Shared.Application.Scenes;
using GeoTools.Shared.Application.Views;
using GeoTools.Shared.Domain.Primatives;
using GeoTools.Shared.Presentation.Views;

namespace GeoTools.Shared.Presentation.Tests;

internal sealed class TestView(ViewId id, Color background = default) : IView
{
    public Color Background { get; } = background;

    public ViewId Id { get; } = id;

    public SceneDto Compose(ViewSize size) => new([], Background);
}
