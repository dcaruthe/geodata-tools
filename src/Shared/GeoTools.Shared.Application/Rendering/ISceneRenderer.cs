using GeoTools.Shared.Application.Scenes;

namespace GeoTools.Shared.Application.Rendering;

public interface ISceneRenderer
{
    void Render(SceneDto scene);
}
