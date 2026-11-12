using System.Globalization;
using GeoTools.Shared.Domain.Geospatial;

namespace GeoTools.Shared.Infrastructure.Persistence;

internal static class StoreMappings
{
    public static long ToUnixMilliseconds(DateTimeOffset value) => value.ToUnixTimeMilliseconds();

    public static DateTimeOffset FromUnixMilliseconds(long value) =>
        DateTimeOffset.FromUnixTimeMilliseconds(value);

    public static string FormatLevels(IReadOnlyList<int> levels) =>
        string.Join(',', levels.Order().Distinct());

    public static int[] ParseLevels(string text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : text.Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                )
                .Select(value =>
                    int.TryParse(
                        value,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var level
                    )
                        ? level
                        : throw new InvalidDataException($"Invalid admin level '{value}'.")
                )
                .ToArray();

    public static string FormatBounds(GeoBounds bounds) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{bounds.MinLongitude:R},{bounds.MinLatitude:R},{bounds.MaxLongitude:R},{bounds.MaxLatitude:R}"
        );

    public static GeoBounds? ParseBounds(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var parts = text.Split(',');
        if (parts.Length != 4)
            throw new InvalidDataException($"Invalid bounds '{text}'.");

        return new GeoBounds(
            double.Parse(parts[0], CultureInfo.InvariantCulture),
            double.Parse(parts[1], CultureInfo.InvariantCulture),
            double.Parse(parts[2], CultureInfo.InvariantCulture),
            double.Parse(parts[3], CultureInfo.InvariantCulture)
        );
    }

    public static T ParseEnum<T>(string value)
        where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new InvalidDataException($"Unknown {typeof(T).Name} value '{value}'.");

    public static string FormatEnum<T>(T value)
        where T : struct, Enum => value.ToString();
}
