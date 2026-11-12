using GeoTools.Shared.Application.Views;

namespace GeoTools.Shared.Presentation.Views;

internal enum ViewKey
{
    Up,
    Down,
    Left,
    Right,
    Enter,
    Backspace,
    Home,
    End,
}

internal interface IViewInputTarget : IView
{
    bool Handle(ViewKey key);
}

internal interface IViewInputRouter
{
    bool Handle(ViewKey key);
}

internal sealed class ViewInputRouter : IViewInputRouter
{
    private readonly INavigationMediator _mediator;
    private readonly Dictionary<ViewId, IViewInputTarget> _targets;

    public ViewInputRouter(IEnumerable<IView> views, INavigationMediator mediator)
    {
        _mediator = mediator;
        _targets = views.OfType<IViewInputTarget>().ToDictionary(view => view.Id);
    }

    public bool Handle(ViewKey key) =>
        _targets.TryGetValue(_mediator.ActiveView, out var target) && target.Handle(key);
}
