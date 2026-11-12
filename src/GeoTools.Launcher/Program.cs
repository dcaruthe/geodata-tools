using GeoTools.Shared.Application.Rendering;
using GeoTools.Shared.Application.Scenes;
using GeoTools.Shared.Infrastructure.Rendering.OpenGl;
using GeoTools.Shared.Presentation.Controls;

var width = 1200;
var height = 800;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--width":
            width = int.Parse(RequireValue(args, ref i));
            break;
        case "--height":
            height = int.Parse(RequireValue(args, ref i));
            break;
        default:
            Console.Error.WriteLine($"Ignoring unknown argument '{args[i]}'.");
            break;
    }
}

Console.WriteLine($"Opening interactive scene window ({width}x{height}).");

SceneWindow.PreferX11();
using var window = new SceneWindow(width, height);
using var surface = new SkiaOpenGlSurface(window);
window.SetScene(new DefaultSceneRenderer(surface), DemoScene.Create(width, height));
window.Run();

static string RequireValue(string[] args, ref int index)
{
    if (index + 1 >= args.Length)
        throw new ArgumentException($"Missing value for '{args[index]}'.");

    return args[++index];
}
