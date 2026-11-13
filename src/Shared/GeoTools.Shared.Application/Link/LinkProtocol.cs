namespace GeoTools.Shared.Application.Link;

public static class LinkProtocol
{
    public const int Version = 1;
    public const int KeyBytes = 32;
    public const int NonceBytes = 12;
    public const int TagBytes = 16;
    public const int MaxFrameBytes = 16 * 1024 * 1024;
    public const string SessionInfo = "geotools-link-v1";
}

public enum LinkFrameKind : byte
{
    Hello = 1,
    Welcome = 2,
    Rotate = 3,
    RotateAck = 4,
    Request = 5,
    Response = 6,
    Close = 7,
}

public enum LinkDirection : byte
{
    ClientToServer = 1,
    ServerToClient = 2,
}

public enum WindowKey
{
    Up,
    Down,
    Left,
    Right,
    Enter,
    Backspace,
    Home,
    End,
}

public enum MaintenanceOperation
{
    Select,
    Adjust,
    Activate,
    Cancel,
}

public enum PipelineAction
{
    Check,
    Repull,
    Extract,
    Cache,
    Reindex,
    Cancel,
    Retry,
    Reset,
}

public enum ScreenshotMode
{
    Scene,
}

public sealed record LinkError(string Code, string Message)
{
    public const string LauncherOffline = "launcher-offline";
    public const string LauncherClosing = "launcher-closing";
    public const string KeyMissing = "key-missing";
    public const string Timeout = "timeout";
    public const string InvalidRequest = "invalid-request";
    public const string Unsupported = "unsupported";
    public const string Internal = "internal";
    public const string Rejected = "rejected";
}

public sealed class LinkUnavailableException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
