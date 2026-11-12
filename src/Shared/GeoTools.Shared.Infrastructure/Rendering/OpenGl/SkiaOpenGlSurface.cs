using GeoTools.Shared.Application.Abstractions.Graphics;
using GeoTools.Shared.Infrastructure.Graphics.Skia;
using OpenTK.Graphics.OpenGL4;
using SkiaSharp;

namespace GeoTools.Shared.Infrastructure.Rendering.OpenGl;

public sealed class SkiaOpenGlSurface : IRenderSurface, IDisposable
{
    private readonly IOpenGlContext _gl;

    private GRGlInterface? _glInterface;
    private GRContext? _grContext;
    private GRBackendRenderTarget? _renderTarget;
    private SKSurface? _surface;

    public SkiaOpenGlSurface(IOpenGlContext gl) => _gl = gl;

    // Render must run on the thread that owns the OpenGL context.
    public void Render(Action<ICanvas> draw)
    {
        _gl.MakeCurrent();

        if (!EnsureSurface())
            return;

        var canvas = new SkiaCanvas(_surface!.Canvas);
        draw(canvas);
        _surface.Canvas.Flush();

        _gl.SwapBuffers();
    }

    private bool EnsureSurface()
    {
        var (w, h) = _gl.Size;
        if (w <= 0 || h <= 0)
            return false;

        if (_surface is not null && _renderTarget!.Width == w && _renderTarget.Height == h)
            return true;

        _surface?.Dispose();
        _surface = null;
        _renderTarget?.Dispose();
        _renderTarget = null;

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        var samples = GL.GetInteger(GetPName.Samples);
        // GL_STENCIL_BITS is gone from the core profile, so ask the default framebuffer's stencil attachment.
        GL.GetFramebufferAttachmentParameter(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.Stencil,
            FramebufferParameterName.FramebufferAttachmentStencilSize,
            out var stencilSize
        );
        var stencilBits = Math.Min(stencilSize, 8);

        // Skia's built-in GL loader returns null on this Linux build, so use the window's loader.
        _glInterface ??= GRGlInterface.CreateOpenGl(_gl.GetProcAddress)
            ?? throw new InvalidOperationException(
                $"Skia could not create a GL interface for {w}x{h} at {samples} samples and {stencilBits} stencil bits."
            );
        _grContext ??= GRContext.CreateGl(_glInterface)
            ?? throw new InvalidOperationException(
                $"Skia could not create a GL context for {w}x{h} at {samples} samples and {stencilBits} stencil bits."
            );

        var info = new GRGlFramebufferInfo(0, (uint)All.Rgba8);
        _renderTarget = new GRBackendRenderTarget(w, h, samples, stencilBits, info);
        _surface = SKSurface.Create(
            _grContext,
            _renderTarget,
            GRSurfaceOrigin.BottomLeft,
            SKColorType.Rgba8888
        )
            ?? throw new InvalidOperationException(
                $"Skia could not create a surface for {w}x{h} at {samples} samples and {stencilBits} stencil bits."
            );

        return true;
    }

    public void Dispose()
    {
        _surface?.Dispose();
        _renderTarget?.Dispose();
        _grContext?.Dispose();
        _glInterface?.Dispose();
    }
}
