using GeoTools.Shared.Application.Scenes;
using GeoTools.Shared.Application.Views;

namespace GeoTools.Shared.Presentation.Views;

public interface IView
{
    ViewId Id { get; }
    SceneDto Compose(ViewSize size);
}
