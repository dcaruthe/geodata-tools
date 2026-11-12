namespace GeoTools.Shared.Application.Views;

internal sealed class NavigationMediator : INavigationMediator
{
    private static readonly ViewId[] Navigable = [ViewId.Map, ViewId.Maintenance];
    private readonly List<ViewId> _order;

    public NavigationMediator(ViewId initial = ViewId.Loading, IReadOnlyList<ViewId>? order = null)
    {
        _order = order is null ? [.. Navigable] : [.. order];
        ActiveView = initial;
    }

    public ViewId ActiveView { get; private set; }

    public event EventHandler<ViewChangedEventArgs>? ViewChanged;

    // Navigation runs on the window's main thread, so no locking is needed.
    public void NavigateTo(ViewId view)
    {
        if (!Enum.IsDefined(view))
            throw new ArgumentOutOfRangeException(nameof(view), view, "Unknown view.");

        if (view == ActiveView)
            return;

        var previous = ActiveView;
        ActiveView = view;
        ViewChanged?.Invoke(this, new ViewChangedEventArgs(previous, view));
    }

    public void NextView() => NavigateTo(Step(1));

    public void PreviousView() => NavigateTo(Step(-1));

    private ViewId Step(int delta)
    {
        var index = _order.IndexOf(ActiveView);
        if (index < 0)
            return _order[0];

        return _order[(index + delta + _order.Count) % _order.Count];
    }
}
