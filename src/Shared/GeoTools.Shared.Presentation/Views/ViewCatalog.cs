using GeoTools.Shared.Application.Views;

namespace GeoTools.Shared.Presentation.Views;

internal sealed class ViewCatalog
{
    private readonly Dictionary<ViewId, IView> _views;

    public ViewCatalog(IEnumerable<IView> views)
    {
        _views = [];
        foreach (var view in views)
        {
            if (!_views.TryAdd(view.Id, view))
                throw new InvalidOperationException($"Two views are registered for {view.Id}.");
        }

        var missing = Enum.GetValues<ViewId>().Where(id => !_views.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException(
                $"No view is registered for {string.Join(", ", missing)}."
            );
    }

    public IView Get(ViewId view) => _views[view];
}
