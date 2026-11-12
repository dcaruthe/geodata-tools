using GeoTools.Shared.Application.Abstractions.Graphics;
using GeoTools.Shared.Application.Rendering;
using GeoTools.Shared.Application.Scenes;
using GeoTools.Shared.Infrastructure.Rendering.OpenGl;
using GeoTools.Shared.Infrastructure.Rendering.Raster;
using GeoTools.Shared.Presentation;
using GeoTools.Shared.Presentation.Controls;
using Microsoft.Extensions.DependencyInjection;

string? outPath = null;
var width = 1200;
var height = 800;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--out":
            outPath = RequireValue(args, ref i);
            break;
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

if (outPath is null)
{
    Console.WriteLine($"Opening interactive scene window ({width}x{height}).");

    using var window = new SceneWindow(width, height);
    using var surface = new SkiaOpenGlSurface(window);
    window.SetScene(new DefaultSceneRenderer(surface), DemoScene.Create(width, height));
    window.Run();
    return;
}

var services = new ServiceCollection();
services.SetupPresentationLayer();
services.AddSingleton(_ => new SkiaRasterSurface(width, height));
services.AddSingleton<IRenderSurface>(provider => provider.GetRequiredService<SkiaRasterSurface>());

using var provider = services.BuildServiceProvider();

var scene = DemoScene.Create(width, height);
provider.GetRequiredService<ISceneRenderer>().Render(scene);

provider.GetRequiredService<SkiaRasterSurface>().Save(outPath);

Console.WriteLine($"Wrote {Path.GetFullPath(outPath)} ({scene.Shapes.Count} shapes).");

static string RequireValue(string[] args, ref int index)
{
    if (index + 1 >= args.Length)
        throw new ArgumentException($"Missing value for '{args[index]}'.");

    return args[++index];
}
