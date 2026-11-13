using System.Security.Cryptography;
using GeoTools.Shared.Application.Storage;

namespace GeoTools.Shared.Infrastructure.Link;

public static class LinkKey
{
    public static string ResolvePath(string configured) => RepositoryPaths.Resolve(configured);

    public static byte[] EnsureCreated(string configuredPath)
    {
        var path = ResolvePath(configuredPath);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        if (File.Exists(path))
            return Load(configuredPath);

        var key = RandomNumberGenerator.GetBytes(32);
        var text = Convert.ToHexStringLower(key) + "\n";
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, text);
            RestrictToOwner(temporary);
            File.Move(temporary, path, overwrite: false);
        }
        catch (IOException)
        {
            // Another launcher created the key between the check and the move. Its file wins.
            if (!File.Exists(path))
                throw;
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }

        return Load(configuredPath);
    }

    public static byte[] Load(string configuredPath)
    {
        var path = ResolvePath(configuredPath);
        var text = File.ReadAllText(path).Trim();
        byte[] key;
        try
        {
            key = Convert.FromHexString(text);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException($"The link key at {path} is not hex.", exception);
        }

        if (key.Length != 32)
            throw new InvalidDataException(
                $"The link key at {path} is {key.Length} bytes. Expected 32."
            );

        return key;
    }

    public static byte[]? TryLoad(string configuredPath)
    {
        try
        {
            var path = ResolvePath(configuredPath);
            return File.Exists(path) ? Load(configuredPath) : null;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    // The socket and the key are owner-only. Windows has no equivalent and cannot run this app.
    internal static void RestrictToOwner(string path)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
