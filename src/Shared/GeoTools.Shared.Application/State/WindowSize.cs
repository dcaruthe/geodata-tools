using System.Globalization;

namespace GeoTools.Shared.Application.State;

public abstract record WindowSize
{
    public static WindowSize Maximized { get; } = new MaximizedWindow();

    public abstract string Display { get; }

    public static WindowSize FromOptions(int width, int height, bool maximized) =>
        maximized ? Maximized : new FixedWindow(width, height);

    public static bool TryParse(string? value, out WindowSize size)
    {
        size = Maximized;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var text = value.Trim();
        if (string.Equals(text, "maximized", StringComparison.OrdinalIgnoreCase))
            return true;

        var parts = text.Split('x');
        if (
            parts.Length != 2
            || !int.TryParse(
                parts[0],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var width
            )
            || !int.TryParse(
                parts[1],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var height
            )
            || width <= 0
            || height <= 0
        )
            return false;

        size = new FixedWindow(width, height);
        return true;
    }
}

public sealed record MaximizedWindow : WindowSize
{
    public override string Display => "maximized";
}

public sealed record FixedWindow(int Width, int Height) : WindowSize
{
    public override string Display => $"{Width}x{Height}";
}
