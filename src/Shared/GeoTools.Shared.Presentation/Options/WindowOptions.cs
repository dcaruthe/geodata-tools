namespace GeoTools.Shared.Presentation.Options;

public sealed class WindowOptions
{
    public const string SectionName = "Window";

    public string Title { get; set; } = "GeoTools";
    public int Width { get; set; } = 1200;
    public int Height { get; set; } = 800;
}
