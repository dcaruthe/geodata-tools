namespace GeoTools.Shared.Application.Abstractions.Graphics;

public interface IRenderSurface
{
    void Render(Action<ICanvas> draw);
}
