using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using GeoTools.Shared.Application.Abstractions.Graphics;
using GeoTools.Shared.Application.Options;
using GeoTools.Shared.Application.Rendering;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Application.Views;
using GeoTools.Shared.Presentation.Link;
using GeoTools.Shared.Presentation.Views;
using Microsoft.Extensions.Logging;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace GeoTools.Shared.Presentation.Controls;

// Needs a live GLFW window and OpenGL context; the verify skill drives the real app instead.
[ExcludeFromCodeCoverage]
internal sealed class SceneWindow : GameWindow, IOpenGlContext
{
    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(2);

    private readonly WindowOptions _options;
    private readonly WindowSize _initialSize;
    private readonly ISettingsStore _settings;
    private readonly IWindowStateStore _windowState;
    private readonly TimeProvider _clock;
    private readonly WindowSizePolicy _sizePolicy;
    private readonly INavigationMediator _mediator;
    private readonly IViewInputRouter _inputRouter;
    private readonly WindowCommandPump _pump;
    private readonly ILogger<SceneWindow> _logger;
    private ISceneRenderer? _renderer;
    private ISceneSource? _source;
    private volatile bool _closeRequested;

    private SceneWindow(
        WindowOptions options,
        WindowSize initialSize,
        ISettingsStore settings,
        IWindowStateStore windowState,
        TimeProvider clock,
        INavigationMediator mediator,
        IViewInputRouter inputRouter,
        WindowCommandPump pump,
        ILogger<SceneWindow> logger
    )
        : base(GameWindowSettings.Default, BuildSettings(options, initialSize))
    {
        _options = options;
        _initialSize = initialSize;
        _settings = settings;
        _windowState = windowState;
        _clock = clock;
        _sizePolicy = new WindowSizePolicy(initialSize);
        _mediator = mediator;
        _inputRouter = inputRouter;
        _pump = pump;
        _logger = logger;

        var fixedSize = initialSize as FixedWindow;
        _windowState.Set(
            new WindowStateSnapshot(
                fixedSize?.Width ?? options.Width,
                fixedSize?.Height ?? options.Height,
                initialSize is MaximizedWindow
            )
        );
    }

    public static SceneWindow Create(
        WindowOptions options,
        WindowSize initialSize,
        ISettingsStore settings,
        IWindowStateStore windowState,
        TimeProvider clock,
        INavigationMediator mediator,
        IViewInputRouter inputRouter,
        WindowCommandPump pump,
        ILogger<SceneWindow> logger
    )
    {
        PreferX11();
        return new SceneWindow(
            options,
            initialSize,
            settings,
            windowState,
            clock,
            mediator,
            inputRouter,
            pump,
            logger
        );
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

    private static NativeWindowSettings BuildSettings(WindowOptions options, WindowSize initial)
    {
        var settings = new NativeWindowSettings
        {
            ClientSize = initial is FixedWindow fixedWindow
                ? new Vector2i(fixedWindow.Width, fixedWindow.Height)
                : new Vector2i(options.Width, options.Height),
            Title = options.Title,
            APIVersion = new Version(3, 3),
            Profile = ContextProfile.Core,
        };

        if (initial is MaximizedWindow)
            settings.WindowState = WindowState.Maximized;

        return settings;
    }

    public void SetSceneSource(ISceneRenderer renderer, ISceneSource source)
    {
        _renderer = renderer;
        _source = source;
    }

    internal void RequestClose() => _closeRequested = true;

    protected override void OnLoad()
    {
        base.OnLoad();

        if (_initialSize is MaximizedWindow)
        {
            if (WindowState != WindowState.Maximized)
                WindowState = WindowState.Maximized;

            _logger.LogInformation(
                "Window started maximized with restore size {Width}x{Height}",
                _options.Width,
                _options.Height
            );
        }
        else
        {
            _logger.LogInformation("Window started at {Size}", _initialSize.Display);
        }
    }

    protected override void OnUpdateFrame(FrameEventArgs args)
    {
        // The link's queued commands run here so navigation, input, and capture all stay on the
        // thread that owns the window and the scene caches.
        _pump.Drain(new ViewSize(ClientSize.X, ClientSize.Y));

        var live = LiveSnapshot();
        foreach (var action in _sizePolicy.Update(_settings.Window, live, _clock.GetUtcNow()))
            Apply(action);

        var current = LiveSnapshot();
        _windowState.Set(new WindowStateSnapshot(current.Width, current.Height, current.Maximized));

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

    protected override void OnClosing(CancelEventArgs e)
    {
        FlushWindowSize();
        base.OnClosing(e);
    }

    protected override void OnKeyDown(KeyboardKeyEventArgs e)
    {
        var viewKey = e.Key switch
        {
            Keys.Up => ViewKey.Up,
            Keys.Down => ViewKey.Down,
            Keys.Left => ViewKey.Left,
            Keys.Right => ViewKey.Right,
            Keys.W => ViewKey.Up,
            Keys.S => ViewKey.Down,
            Keys.A => ViewKey.Left,
            Keys.D => ViewKey.Right,
            Keys.Equal or Keys.KeyPadAdd => ViewKey.ZoomIn,
            Keys.Minus or Keys.KeyPadSubtract => ViewKey.ZoomOut,
            Keys.Enter or Keys.KeyPadEnter => ViewKey.Enter,
            Keys.Backspace => ViewKey.Backspace,
            Keys.Home => ViewKey.Home,
            Keys.End => ViewKey.End,
            _ => (ViewKey?)null,
        };

        if (
            viewKey is { } key
            && _inputRouter.Handle(key, new ViewSize(ClientSize.X, ClientSize.Y))
        )
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

    private WindowSizeSnapshot LiveSnapshot() =>
        new(ClientSize.X, ClientSize.Y, WindowState == WindowState.Maximized);

    private void Apply(WindowAction action)
    {
        switch (action.Kind)
        {
            case WindowActionKind.Apply when action.Size is MaximizedWindow:
                if (WindowState != WindowState.Maximized)
                    WindowState = WindowState.Maximized;
                _logger.LogInformation("Window size applied: maximized");
                break;
            case WindowActionKind.Apply when action.Size is FixedWindow fixedWindow:
                WindowState = WindowState.Normal;
                ClientSize = new Vector2i(fixedWindow.Width, fixedWindow.Height);
                _logger.LogInformation("Window size applied: {Size}", fixedWindow.Display);
                break;
            case WindowActionKind.Persist:
                _ = PersistAsync(action.Size);
                break;
        }
    }

    private async Task PersistAsync(WindowSize size)
    {
        try
        {
            var result = await _settings.SetWindowSizeAsync(size);
            if (!result.Accepted)
                _logger.LogWarning("The window size was not saved: {Error}", result.Error);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not save the window size.");
        }
    }

    private void FlushWindowSize()
    {
        if (_sizePolicy.Flush(LiveSnapshot()) is not { } action)
            return;

        try
        {
            if (!_settings.SetWindowSizeAsync(action.Size).Wait(FlushTimeout))
                _logger.LogWarning("Timed out saving the window size.");
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not save the window size.");
        }
    }

    (int, int) IOpenGlContext.Size => (ClientSize.X, ClientSize.Y);

    void IOpenGlContext.MakeCurrent() => MakeCurrent();

    void IOpenGlContext.SwapBuffers() => SwapBuffers();

    IntPtr IOpenGlContext.GetProcAddress(string name) => GLFW.GetProcAddress(name);
}
