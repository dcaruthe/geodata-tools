using GeoTools.Shared.Application.State;

namespace GeoTools.Shared.Presentation.Views;

internal static class WindowSizePresets
{
    public static IReadOnlyList<WindowSize> All { get; } =
    [
        WindowSize.Maximized,
        new FixedWindow(1280, 800),
        new FixedWindow(1440, 900),
        new FixedWindow(1600, 900),
        new FixedWindow(1920, 1080),
    ];

    public static WindowSize Next(WindowSize current, int direction)
    {
        var presets = new List<WindowSize>(All);
        if (current is FixedWindow && !presets.Contains(current))
            presets.Insert(1, current);

        var index = presets.IndexOf(current);
        if (index < 0)
            index = 0;

        return presets[(index + direction + presets.Count) % presets.Count];
    }
}
