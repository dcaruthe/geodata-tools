using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace GeoTools.Api.Tests;

public sealed class McpTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task The_mcp_server_lists_and_runs_the_geotools_tools()
    {
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri("http://localhost/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
            },
            factory.CreateClient()
        );

        await using var client = await McpClient.CreateAsync(transport);
        var tools = await client.ListToolsAsync();

        tools
            .Select(tool => tool.Name)
            .Should()
            .Contain([
                "state",
                "logs",
                "screenshot",
                "navigate",
                "maintenance",
                "borders",
                "set_window",
            ]);

        var result = await client.CallToolAsync("state", new Dictionary<string, object?>());

        result.IsError.Should().NotBe(true);
        var text = result.Content.OfType<TextContentBlock>().First().Text;
        text.Should().Contain("offline");
        text.Should().Contain("artifactCount");

        var settings = await client.CallToolAsync("settings", new Dictionary<string, object?>());
        settings.Content.OfType<TextContentBlock>().First().Text.Should().Contain("\"window\"");
    }

    [Fact]
    public async Task The_set_window_tool_rejects_zero_dimensions()
    {
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri("http://localhost/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
            },
            factory.CreateClient()
        );

        await using var client = await McpClient.CreateAsync(transport);
        var result = await client.CallToolAsync(
            "set_window",
            new Dictionary<string, object?>
            {
                ["maximized"] = false,
                ["width"] = 0,
                ["height"] = 0,
            }
        );

        result.IsError.Should().BeTrue();
        result.Content.OfType<TextContentBlock>().First().Text.Should().Contain("Width and height");
    }

    [Fact]
    public async Task The_borders_tool_returns_the_fixture_summary()
    {
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri("http://localhost/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
            },
            factory.CreateClient()
        );

        await using var client = await McpClient.CreateAsync(transport);
        var result = await client.CallToolAsync(
            "borders",
            new Dictionary<string, object?> { ["format"] = "summary" }
        );

        result.IsError.Should().NotBe(true);
        result.Content.OfType<TextContentBlock>().First().Text.Should().Contain("polylineCount");
    }
}
