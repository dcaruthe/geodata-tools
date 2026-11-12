using GeoTools.Shared.Application.Abstractions.Graphics;
using GeoTools.Shared.Application.Rendering;
using GeoTools.Shared.Application.Scenes;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace GeoTools.Shared.Presentation.Controls;

public sealed class SceneWindow : GameWindow, IOpenGlContext
{
    private ISceneRenderer? _renderer;
    private SceneDto? _scene;

    public SceneWindow(int width, int height)
        : base(
            GameWindowSettings.Default,
            new NativeWindowSettings
            {
                ClientSize = new Vector2i(width, height),
                Title = "GeoTools scene",
                APIVersion = new Version(3, 3),
                Profile = ContextProfile.Core,
            }
        ) { }

    // GLFW selects Wayland by default, and its Wayland path loads libdecor, which loads GTK when
    // the compositor wants client-side decorations. This window needs no toolkit, and GTK aborts
    // inside glfwInit when the desktop's settings schema is inconsistent. XWayland serves
    // DISPLAY whenever the session offers one, so take that path and leave GTK out of the process.
    public static void PreferX11()
    {
        if (
            OperatingSystem.IsLinux()
            && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))
        )
            GLFW.InitHint(InitHintPlatform.Platform, Platform.X11);
    }

    public void SetScene(ISceneRenderer renderer, SceneDto scene)
    {
        _renderer = renderer;
        _scene = scene;
    }

    protected override void OnRenderFrame(FrameEventArgs args)
    {
        if (_renderer is not null && _scene is not null)
            _renderer.Render(_scene);

        base.OnRenderFrame(args);
    }

    (int, int) IOpenGlContext.Size => (ClientSize.X, ClientSize.Y);

    void IOpenGlContext.MakeCurrent() => MakeCurrent();

    void IOpenGlContext.SwapBuffers() => SwapBuffers();

    IntPtr IOpenGlContext.GetProcAddress(string name) => GLFW.GetProcAddress(name);
}
