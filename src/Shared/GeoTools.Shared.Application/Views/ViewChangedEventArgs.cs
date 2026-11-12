namespace GeoTools.Shared.Application.Views;

public sealed class ViewChangedEventArgs(ViewId previous, ViewId current) : EventArgs
{
    public ViewId Previous { get; } = previous;
    public ViewId Current { get; } = current;
}
