using System.Diagnostics.CodeAnalysis;
using GeoTools.Shared.Application.Abstractions.Graphics;
using GeoTools.Shared.Application.Rendering;
using GeoTools.Shared.Application.Views;
using GeoTools.Shared.Presentation.Options;
using GeoTools.Shared.Presentation.Views;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace GeoTools.Shared.Presentation.Controls;

// Needs a live GLFW window and OpenGL context; the verify skill drives the real app instead.
[ExcludeFromCodeCoverage]
internal sealed class SceneWindow : GameWindow, IOpenGlContext
{
    private readonly INavigationMediator _mediator;
    private readonly IViewInputRouter _inputRouter;
    private ISceneRenderer? _renderer;
    private ISceneSource? _source;
    private volatile bool _closeRequested;

    private SceneWindow(
        WindowOptions options,
        INavigationMediator mediator,
        IViewInputRouter inputRouter
    )
        : base(
            GameWindowSettings.Default,
            new NativeWindowSettings
            {
                ClientSize = new Vector2i(options.Width, options.Height),
                Title = options.Title,
                APIVersion = new Version(3, 3),
                Profile = ContextProfile.Core,
            }
        )
    {
        _mediator = mediator;
        _inputRouter = inputRouter;
    }

    public static SceneWindow Create(
        WindowOptions options,
        INavigationMediator mediator,
        IViewInputRouter inputRouter
    )
    {
        PreferX11();
        return new SceneWindow(options, mediator, inputRouter);
    }

    // GLFW selects Wayland by default, and its Wayland path loads libdecor, which loads GTK when
    // the compositor wants client-side decorations. This window needs no toolkit, and GTK aborts
    // inside glfwInit when the desktop's settings schema is inconsistent. XWayland serves
    // DISPLAY whenever the session offers one, so take that path and leave GTK out of the process.
    private static void PreferX11()
    {
        if (
            OperatingSystem.IsLinux()
            && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))
        )
            GLFW.InitHint(InitHintPlatform.Platform, Platform.X11);
    }

    public void SetSceneSource(ISceneRenderer renderer, ISceneSource source)
    {
        _renderer = renderer;
        _source = source;
    }

    internal void RequestClose() => _closeRequested = true;

    protected override void OnUpdateFrame(FrameEventArgs args)
    {
        if (_closeRequested)
            Close();

        base.OnUpdateFrame(args);
    }

    protected override void OnRenderFrame(FrameEventArgs args)
    {
        if (_renderer is not null && _source is not null)
            _renderer.Render(_source.Compose(new ViewSize(ClientSize.X, ClientSize.Y)));

        base.OnRenderFrame(args);
    }

    protected override void OnKeyDown(KeyboardKeyEventArgs e)
    {
        var viewKey = e.Key switch
        {
            Keys.Up => ViewKey.Up,
            Keys.Down => ViewKey.Down,
            Keys.Left => ViewKey.Left,
            Keys.Right => ViewKey.Right,
            Keys.Enter or Keys.KeyPadEnter => ViewKey.Enter,
            Keys.Backspace => ViewKey.Backspace,
            Keys.Home => ViewKey.Home,
            Keys.End => ViewKey.End,
            _ => (ViewKey?)null,
        };

        if (viewKey is { } key && _inputRouter.Handle(key))
        {
            base.OnKeyDown(e);
            return;
        }

        switch (e.Key)
        {
            case Keys.Escape:
                Close();
                break;
            case Keys.Tab:
                if (e.Modifiers.HasFlag(KeyModifiers.Shift))
                    _mediator.PreviousView();
                else
                    _mediator.NextView();
                break;
            default:
                if (ViewShortcuts.TryGetView(e.Key, out var view))
                    _mediator.NavigateTo(view);
                break;
        }

        base.OnKeyDown(e);
    }

    (int, int) IOpenGlContext.Size => (ClientSize.X, ClientSize.Y);

    void IOpenGlContext.MakeCurrent() => MakeCurrent();

    void IOpenGlContext.SwapBuffers() => SwapBuffers();

    IntPtr IOpenGlContext.GetProcAddress(string name) => GLFW.GetProcAddress(name);
}
