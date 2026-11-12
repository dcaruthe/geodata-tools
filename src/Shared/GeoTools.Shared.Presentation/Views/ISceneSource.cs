using GeoTools.Shared.Application.Scenes;

namespace GeoTools.Shared.Presentation.Views;

public interface ISceneSource
{
    SceneDto Compose(ViewSize size);
}
