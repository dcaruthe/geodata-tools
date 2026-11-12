namespace GeoTools.Shared.Application.Views;

public interface INavigationMediator
{
    ViewId ActiveView { get; }
    void NavigateTo(ViewId view);
    void NextView();
    void PreviousView();
    event EventHandler<ViewChangedEventArgs>? ViewChanged;
}
