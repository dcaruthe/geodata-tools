using GeoTools.Shared.Application.Scenes;
using GeoTools.Shared.Application.Views;
using Microsoft.Extensions.Logging;

namespace GeoTools.Shared.Presentation.Views;

internal sealed class ActiveViewSceneSource : ISceneSource
{
    private readonly ViewCatalog _catalog;
    private readonly ILogger<ActiveViewSceneSource> _logger;
    private IView _activeView;

    public ActiveViewSceneSource(
        ViewCatalog catalog,
        INavigationMediator mediator,
        ILogger<ActiveViewSceneSource> logger
    )
    {
        _catalog = catalog;
        _logger = logger;
        _activeView = catalog.Get(mediator.ActiveView);
        mediator.ViewChanged += OnViewChanged;
    }

    public SceneDto Compose(ViewSize size) => _activeView.Compose(size);

    private void OnViewChanged(object? sender, ViewChangedEventArgs args)
    {
        _activeView = _catalog.Get(args.Current);
        _logger.LogInformation(
            "Switched view {Previous} -> {Current}",
            args.Previous,
            args.Current
        );
    }
}
