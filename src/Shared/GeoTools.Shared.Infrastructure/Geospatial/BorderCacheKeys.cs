using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace GeoTools.Shared.Infrastructure.Geospatial;

internal static class BorderCacheKeys
{
    public static string Compute(string file, double tolerance, IReadOnlyList<int> levels)
    {
        var info = new FileInfo(file);
        var text = string.Join(
            '|',
            Path.GetFileName(file),
            info.Length.ToString(CultureInfo.InvariantCulture),
            info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture),
            tolerance.ToString("R", CultureInfo.InvariantCulture),
            GeometryBlobCodec.Version.ToString(CultureInfo.InvariantCulture),
            string.Join(',', levels.Order().Distinct())
        );

        return Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))
            .ToLowerInvariant();
    }

    public static string Fingerprint(IReadOnlyList<string> keys) =>
        Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', keys))))
            .ToLowerInvariant();
}
