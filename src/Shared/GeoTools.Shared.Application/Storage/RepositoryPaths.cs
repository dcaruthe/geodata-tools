namespace GeoTools.Shared.Application.Storage;

public static class RepositoryPaths
{
    public static string FindRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "geodata-tools.slnx")))
                    return directory.FullName;

                directory = directory.Parent;
            }
        }

        return Directory.GetCurrentDirectory();
    }

    public static string Resolve(string configured) => Resolve(configured, FindRoot());

    public static string Resolve(string configured, string root) =>
        Path.IsPathRooted(configured)
            ? Path.GetFullPath(configured)
            : Path.GetFullPath(Path.Combine(root, configured));
}
