using System.Text.Json;
using System.Text.Json.Serialization;
using GeoTools.Shared.Application.Views;

namespace GeoTools.Shared.Application.Link;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(GetStateRequest), "get-state")]
[JsonDerivedType(typeof(GetLogsRequest), "get-logs")]
[JsonDerivedType(typeof(GetMaintenanceRequest), "get-maintenance")]
[JsonDerivedType(typeof(GetSettingsRequest), "get-settings")]
[JsonDerivedType(typeof(ScreenshotRequest), "screenshot")]
[JsonDerivedType(typeof(NavigateRequest), "navigate")]
[JsonDerivedType(typeof(PressKeysRequest), "press-keys")]
[JsonDerivedType(typeof(MaintenanceCommandRequest), "maintenance")]
[JsonDerivedType(typeof(PipelineCommandRequest), "pipeline")]
[JsonDerivedType(typeof(SetDisplayLevelsRequest), "set-display-levels")]
[JsonDerivedType(typeof(SetExtractLevelsRequest), "set-extract-levels")]
[JsonDerivedType(typeof(SetToleranceRequest), "set-tolerance")]
[JsonDerivedType(typeof(SetFeatureFlagRequest), "set-feature-flag")]
[JsonDerivedType(typeof(ResetSettingsRequest), "reset-settings")]
public abstract record LinkRequest(string Id);

public sealed record GetStateRequest(string Id) : LinkRequest(Id);

public sealed record GetLogsRequest(string Id, string? Level, int Limit, long? After)
    : LinkRequest(Id);

public sealed record GetMaintenanceRequest(string Id) : LinkRequest(Id);

public sealed record GetSettingsRequest(string Id) : LinkRequest(Id);

public sealed record ScreenshotRequest(string Id, int? Width, int? Height, ScreenshotMode Mode)
    : LinkRequest(Id);

public sealed record NavigateRequest(string Id, ViewId View) : LinkRequest(Id);

public sealed record PressKeysRequest(string Id, WindowKey[] Keys) : LinkRequest(Id);

public sealed record MaintenanceCommandRequest(
    string Id,
    MaintenanceOperation Operation,
    string? RowId,
    int Direction,
    bool Confirm
) : LinkRequest(Id);

public sealed record PipelineCommandRequest(string Id, PipelineAction Action, bool Confirm)
    : LinkRequest(Id);

public sealed record SetDisplayLevelsRequest(string Id, int[] Levels) : LinkRequest(Id);

public sealed record SetExtractLevelsRequest(string Id, int[] Levels) : LinkRequest(Id);

public sealed record SetToleranceRequest(string Id, double Tolerance) : LinkRequest(Id);

public sealed record SetFeatureFlagRequest(string Id, string Flag, bool Enabled) : LinkRequest(Id);

public sealed record ResetSettingsRequest(string Id) : LinkRequest(Id);

public sealed record LinkResponse(string Id, bool Ok, JsonElement? Payload, LinkError? Error)
{
    public static LinkResponse Success(string id, object? payload = null) =>
        new(
            id,
            true,
            payload is null
                ? null
                : JsonSerializer.SerializeToElement(payload, payload.GetType(), LinkJson.Options),
            null
        );

    public static LinkResponse Failure(string id, LinkError error) => new(id, false, null, error);

    public T? ReadPayload<T>() =>
        Payload is { ValueKind: not JsonValueKind.Null } payload
            ? payload.Deserialize<T>(LinkJson.Options)
            : default;
}

public static class LinkJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
