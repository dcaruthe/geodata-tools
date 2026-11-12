using GeoTools.Shared.Application.Views;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace GeoTools.Shared.Presentation.Controls;

internal static class ViewShortcuts
{
    private static readonly Dictionary<Keys, ViewId> Views = new()
    {
        [Keys.D1] = ViewId.Map,
        [Keys.D2] = ViewId.Settings,
    };

    public static bool TryGetView(Keys key, out ViewId view) => Views.TryGetValue(key, out view);
}
