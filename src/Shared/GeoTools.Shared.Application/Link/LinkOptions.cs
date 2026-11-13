namespace GeoTools.Shared.Application.Link;

public sealed class LinkOptions
{
    public const string SectionName = "Link";

    public bool Enabled { get; set; } = true;

    public string SocketPath { get; set; } = "geodata/state/launcher.sock";

    public string KeyPath { get; set; } = "geodata/state/launcher.key";

    // The session key is replaced on this interval. It defaults to fifteen minutes.
    public int RotateSeconds { get; set; } = 900;

    public int CommandTimeoutSeconds { get; set; } = 10;

    public int HandshakeTimeoutSeconds { get; set; } = 5;
}
