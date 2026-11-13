using System.Text.Json;
using FluentAssertions;
using GeoTools.Shared.Application.Link;
using GeoTools.Shared.Application.Views;
using GeoTools.Shared.Domain.Geospatial;

namespace GeoTools.Shared.Application.Tests;

public sealed class LinkContractTests
{
    public static TheoryData<LinkRequest> Requests =>
        new()
        {
            new GetStateRequest("1"),
            new GetLogsRequest("2", "warning", 50, 7),
            new GetMaintenanceRequest("3"),
            new GetSettingsRequest("4"),
            new ScreenshotRequest("5", 800, 600, ScreenshotMode.Scene),
            new NavigateRequest("6", ViewId.Maintenance),
            new PressKeysRequest("7", [WindowKey.Down, WindowKey.Enter]),
            new MaintenanceCommandRequest(
                "8",
                MaintenanceOperation.Adjust,
                "displayLevels",
                1,
                false
            ),
            new PipelineCommandRequest("9", PipelineAction.Repull, true),
            new SetDisplayLevelsRequest("10", [2, 4]),
            new SetExtractLevelsRequest("11", [2]),
            new SetToleranceRequest("12", 0.005),
            new SetFeatureFlagRequest("13", "MapGrid", false),
            new ResetSettingsRequest("14"),
            new SetWindowSizeRequest("15", false, 1600, 900),
            new GetMapStateRequest("16"),
            new FocusPointRequest("17", 10.45, 51.16),
            new FocusRegionRequest("18", "region:51477"),
            new BlurFocusRequest("19"),
            new PanRequest("20", 64, 0),
            new ZoomRequest("21", 1),
        };

    [Theory]
    [MemberData(nameof(Requests))]
    public void A_request_round_trips_with_its_discriminator(LinkRequest request)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes<LinkRequest>(request, LinkJson.Options);
        var parsed = JsonSerializer.Deserialize<LinkRequest>(json, LinkJson.Options);

        JsonDocument
            .Parse(json)
            .RootElement.GetProperty("kind")
            .GetString()
            .Should()
            .NotBeNullOrEmpty();
        parsed.Should().BeEquivalentTo(request);
        parsed.GetType().Should().Be(request.GetType());
    }

    [Fact]
    public void A_response_payload_round_trips_through_json()
    {
        var settings = new SettingsDto(
            [2, 4],
            [2],
            0.002,
            new Dictionary<string, bool> { ["MapGrid"] = true },
            new WindowSizeDto(false, 1600, 900)
        );

        var response = LinkResponse.Success("abc", settings);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(response, LinkJson.Options);
        var parsed = JsonSerializer.Deserialize<LinkResponse>(bytes, LinkJson.Options)!;

        parsed.Ok.Should().BeTrue();
        parsed.Error.Should().BeNull();
        parsed.ReadPayload<SettingsDto>().Should().BeEquivalentTo(settings);
    }

    [Fact]
    public void A_map_state_payload_round_trips_through_json()
    {
        var state = new MapStateDto(
            10.45,
            51.16,
            3.5,
            2.0,
            new GeoBounds(5, 47, 16, 55),
            [new FocusedRegionDto("region:51477", "Germany", 2, new GeoBounds(5, 47, 16, 55))],
            [2, 4],
            "Focused Germany",
            1200,
            800
        );

        var response = LinkResponse.Success("abc", state);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(response, LinkJson.Options);
        var parsed = JsonSerializer.Deserialize<LinkResponse>(bytes, LinkJson.Options)!;

        parsed.Ok.Should().BeTrue();
        parsed.ReadPayload<MapStateDto>().Should().BeEquivalentTo(state);
    }

    [Fact]
    public void An_error_response_carries_its_code()
    {
        var response = LinkResponse.Failure(
            "abc",
            new LinkError(LinkError.LauncherOffline, "not running")
        );

        var bytes = JsonSerializer.SerializeToUtf8Bytes(response, LinkJson.Options);
        var parsed = JsonSerializer.Deserialize<LinkResponse>(bytes, LinkJson.Options)!;

        parsed.Ok.Should().BeFalse();
        parsed.Error!.Code.Should().Be(LinkError.LauncherOffline);
        parsed.ReadPayload<SettingsDto>().Should().BeNull();
    }
}
