using GeoTools.Shared.Application.State;

namespace GeoTools.Shared.Presentation.Controls;

internal sealed record WindowSizeSnapshot(int Width, int Height, bool Maximized);

internal enum WindowActionKind
{
    Apply,
    Persist,
}

internal readonly record struct WindowAction(WindowActionKind Kind, WindowSize Size);

// Compares the persisted window setting with the live window. The render loop owns the window,
// so the policy takes plain values and returns the actions to run on that thread.
internal sealed class WindowSizePolicy
{
    private static readonly TimeSpan ApplySettle = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan ApplyIgnore = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan PersistStable = TimeSpan.FromSeconds(1);
    private const int DimensionTolerance = 1;
    private const int MinimumWidth = 320;
    private const int MinimumHeight = 240;
    private const int MaximumWidth = 7680;
    private const int MaximumHeight = 4320;

    private WindowSize _applied;
    private WindowSize? _pending;
    private DateTimeOffset _pendingSince;
    private DateTimeOffset? _appliedAt;
    private WindowSizeSnapshot? _observed;
    private DateTimeOffset _observedSince;

    public WindowSizePolicy(WindowSize initial) => _applied = initial;

    public IReadOnlyList<WindowAction> Update(
        WindowSize desired,
        WindowSizeSnapshot live,
        DateTimeOffset now
    )
    {
        if (desired != _applied)
        {
            if (_pending != desired)
            {
                _pending = desired;
                _pendingSince = now;
            }
            else if (now - _pendingSince >= ApplySettle)
            {
                _applied = desired;
                _pending = null;
                _appliedAt = now;
                _observed = null;
                return [new WindowAction(WindowActionKind.Apply, desired)];
            }
        }
        else
        {
            _pending = null;
        }

        return Observe(live, now) ? [new WindowAction(WindowActionKind.Persist, SizeOf(live))] : [];
    }

    public WindowAction? Flush(WindowSizeSnapshot live)
    {
        if (IsSame(_applied, live) || !CanPersist(live))
            return null;

        var size = SizeOf(live);
        _applied = size;
        return new WindowAction(WindowActionKind.Persist, size);
    }

    private bool Observe(WindowSizeSnapshot live, DateTimeOffset now)
    {
        if (_appliedAt is { } appliedAt && now - appliedAt < ApplyIgnore)
        {
            _observed = null;
            return false;
        }

        if (IsSame(_applied, live) || !CanPersist(live))
        {
            _observed = null;
            return false;
        }

        if (_observed != live)
        {
            _observed = live;
            _observedSince = now;
            return false;
        }

        if (now - _observedSince < PersistStable)
            return false;

        _applied = SizeOf(live);
        _appliedAt = now;
        _observed = null;
        return true;
    }

    private static bool IsSame(WindowSize applied, WindowSizeSnapshot live) =>
        applied switch
        {
            MaximizedWindow => live.Maximized,
            FixedWindow fixedWindow
                when !live.Maximized
                    && Math.Abs(fixedWindow.Width - live.Width) <= DimensionTolerance
                    && Math.Abs(fixedWindow.Height - live.Height) <= DimensionTolerance => true,
            _ => false,
        };

    private static bool CanPersist(WindowSizeSnapshot live)
    {
        if (live.Width <= 0 || live.Height <= 0)
            return false;

        if (live.Maximized)
            return true;

        return live.Width is >= MinimumWidth and <= MaximumWidth
            && live.Height is >= MinimumHeight and <= MaximumHeight;
    }

    private static WindowSize SizeOf(WindowSizeSnapshot live) =>
        live.Maximized ? WindowSize.Maximized : new FixedWindow(live.Width, live.Height);
}
