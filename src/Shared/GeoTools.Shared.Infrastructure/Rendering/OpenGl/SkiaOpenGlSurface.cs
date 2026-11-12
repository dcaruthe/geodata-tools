using GeoTools.Shared.Application.Abstractions.Graphics;
using GeoTools.Shared.Infrastructure.Graphics.Skia;
using OpenTK.Graphics.OpenGL4;
using SkiaSharp;

namespace GeoTools.Shared.Infrastructure.Rendering.OpenGl;

public sealed class SkiaOpenGlSurface : IRenderSurface, IDisposable
{
    private readonly IOpenGlContext _gl;
    private readonly object _gate = new();

    private GRContext? _grContext;
    private GRBackendRenderTarget? _renderTarget;
    private SKSurface? _surface;

    public SkiaOpenGlSurface(IOpenGlContext gl) => _gl = gl;

    public void Render(Action<ICanvas> draw)
    {
        // OpenGL contexts are thread-affine; Render must be on the UI/GL thread.
        _gl.MakeCurrent();

        lock (_gate)
        {
            EnsureSurface();
            var canvas = new SkiaCanvas(_surface!.Canvas);
            draw(canvas);
            _surface.Canvas.Flush();
        }

        _gl.SwapBuffers();
    }

    private void EnsureSurface()
    {
        var (w, h) = _gl.Size;
        if (w <= 0 || h <= 0)
            return;

        if (_surface is not null && _renderTarget!.Width == w && _renderTarget.Height == h)
            return;

        _surface?.Dispose();
        _renderTarget?.Dispose();

        // First-time GPU context creation; must happen on the GL thread.
        _grContext ??= GRContext.CreateGl(GRGlInterface.Create());

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        var info = new GRGlFramebufferInfo(0, (uint)All.Rgba8);
        _renderTarget = new GRBackendRenderTarget(w, h, 0, 8, info);
        _surface = SKSurface.Create(
            _grContext,
            _renderTarget,
            GRSurfaceOrigin.BottomLeft,
            SKColorType.Rgba8888
        );
    }

    public void Dispose()
    {
        _surface?.Dispose();
        _renderTarget?.Dispose();
        _grContext?.Dispose();
    }
}
