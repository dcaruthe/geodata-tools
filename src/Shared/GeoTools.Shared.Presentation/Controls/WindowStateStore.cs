namespace GeoTools.Shared.Presentation.Controls;

internal sealed record WindowStateSnapshot(int Width, int Height, bool Maximized);

internal interface IWindowStateStore
{
    WindowStateSnapshot Current { get; }

    long Version { get; }

    void Set(WindowStateSnapshot state);
}

internal sealed class WindowStateStore : IWindowStateStore
{
    private WindowStateSnapshot _current = new(0, 0, false);
    private long _version;

    public WindowStateSnapshot Current => Volatile.Read(ref _current);

    public long Version => Interlocked.Read(ref _version);

    public void Set(WindowStateSnapshot state)
    {
        Volatile.Write(ref _current, state);
        Interlocked.Increment(ref _version);
    }
}
