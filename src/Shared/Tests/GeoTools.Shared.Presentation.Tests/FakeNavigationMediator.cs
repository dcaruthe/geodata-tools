using GeoTools.Shared.Application.Views;

namespace GeoTools.Shared.Presentation.Tests;

internal sealed class FakeNavigationMediator : INavigationMediator
{
    public ViewId ActiveView { get; private set; } = ViewId.Loading;

    public event EventHandler<ViewChangedEventArgs>? ViewChanged;

    public void NavigateTo(ViewId view)
    {
        if (view == ActiveView)
            return;

        var previous = ActiveView;
        ActiveView = view;
        ViewChanged?.Invoke(this, new ViewChangedEventArgs(previous, view));
    }

    public void NextView() =>
        NavigateTo(ActiveView == ViewId.Maintenance ? ViewId.Map : ViewId.Maintenance);

    public void PreviousView() =>
        NavigateTo(ActiveView == ViewId.Map ? ViewId.Maintenance : ViewId.Map);
}
