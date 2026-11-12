namespace GeoTools.Shared.Application.Messaging;

public static class MessageTopics
{
    public const string BootstrapRunStarted = "bootstrap.run.started";
    public const string BootstrapStepChanged = "bootstrap.step.changed";
    public const string BootstrapRunFinished = "bootstrap.run.finished";
    public const string BordersAvailable = "borders.available";
    public const string SettingsChanged = "runtime.settings.changed";
    public const string FeatureFlagChanged = "runtime.flag.changed";
    public const string RebuildRequested = "command.rebuild.requested";
    public const string CancelRequested = "command.cancel.requested";
    public const string SubscriptionFaulted = "subscription.faulted";

    public static IReadOnlyList<string> All { get; } =
    [
        BootstrapRunStarted,
        BootstrapStepChanged,
        BootstrapRunFinished,
        BordersAvailable,
        SettingsChanged,
        FeatureFlagChanged,
        RebuildRequested,
        CancelRequested,
        SubscriptionFaulted,
    ];
}
