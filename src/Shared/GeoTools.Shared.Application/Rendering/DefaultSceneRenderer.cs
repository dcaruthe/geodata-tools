using GeoTools.Shared.Application.Abstractions.Graphics;
using GeoTools.Shared.Application.Scenes;

namespace GeoTools.Shared.Application.Rendering;

public sealed class DefaultSceneRenderer : ISceneRenderer
{
    private readonly IRenderSurface _surface;

    public DefaultSceneRenderer(IRenderSurface surface)
    {
        _surface = surface;
    }

    public void Render(SceneDto scene)
    {
        _surface.Render(canvas =>
        {
            canvas.Clear(scene.Background);
            foreach (var shape in scene.Shapes)
            {
                switch (shape)
                {
                    case LineDto line:
                        canvas.DrawLine(line.From, line.To, line.Color, line.Thickness);
                        break;
                    case CircleDto circle:
                        canvas.DrawCircle(
                            circle.Center,
                            circle.Radius,
                            circle.Color,
                            circle.Thickness,
                            circle.Filled
                        );
                        break;
                    case PolygonDto polygon:
                        canvas.DrawPolygon(
                            polygon.Points,
                            polygon.Color,
                            polygon.Thickness,
                            polygon.Filled
                        );
                        break;
                    case PolylineDto polyline:
                        canvas.DrawPolyline(polyline.Points, polyline.Color, polyline.Thickness);
                        break;
                    case TextDto text:
                        canvas.DrawText(text.Position, text.Text, text.Size, text.Color);
                        break;
                    default:
                        throw new NotSupportedException(
                            $"Unsupported shape {shape.GetType().Name}."
                        );
                }
            }
        });
    }
}
