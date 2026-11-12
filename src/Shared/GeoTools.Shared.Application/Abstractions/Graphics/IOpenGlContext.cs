namespace GeoTools.Shared.Application.Abstractions.Graphics;

public interface IOpenGlContext
{
    void MakeCurrent();
    void SwapBuffers();
    (int Width, int Height) Size { get; }
    IntPtr GetProcAddress(string name);
}
