using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace GeoTools.Api.Tests;

public sealed class ApiSurfaceTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private HttpClient Client => factory.CreateClient();

    [Fact]
    public async Task Health_reports_the_database_and_an_offline_launcher()
    {
        var json = await GetJson("/health");

        json.GetProperty("status").GetString().Should().Be("ok");
        json.GetProperty("launcher").GetString().Should().Be("offline");
        json.GetProperty("databaseAvailable").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task State_reports_the_local_settings_while_the_launcher_is_down()
    {
        var json = await GetJson("/api/state");

        json.GetProperty("launcherStatus").GetString().Should().Be("offline");
        json.GetProperty("launcherReason").GetString().Should().NotBeNullOrEmpty();
        json.GetProperty("local")
            .GetProperty("settings")
            .GetProperty("tolerance")
            .GetDouble()
            .Should()
            .Be(0.002);
        json.GetProperty("local")
            .GetProperty("latestRun")
            .GetProperty("status")
            .GetString()
            .Should()
            .Be("Succeeded");
    }

    [Fact]
    public async Task The_latest_run_comes_from_the_read_only_database()
    {
        var json = await GetJson("/api/runs/latest");

        json.GetProperty("run").GetProperty("status").GetString().Should().Be("Succeeded");
        json.GetProperty("steps")[0].GetProperty("key").GetString().Should().Be("pull:africa");
    }

    [Fact]
    public async Task Artifacts_are_listed()
    {
        var json = await GetJson("/api/artifacts");

        json.GetArrayLength().Should().Be(1);
        json[0].GetProperty("kind").GetString().Should().Be("Borders");
        json[0].GetProperty("region").GetString().Should().Be("africa");
    }

    [Fact]
    public async Task Regions_report_the_border_fixture()
    {
        var json = await GetJson("/api/regions");

        json.GetProperty("regions")[0].GetProperty("name").GetString().Should().Be("africa");
        json.GetProperty("regions")[0].GetProperty("hasBorders").GetBoolean().Should().BeTrue();
        json.GetProperty("geometryAvailable").GetBoolean().Should().BeTrue();
        json.GetProperty("availableAdminLevels")
            .EnumerateArray()
            .Select(level => level.GetInt32())
            .Should()
            .Equal(2, 4);
        json.GetProperty("polylineCount").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task A_border_summary_counts_the_fixture()
    {
        var json = await GetJson("/api/borders");

        json.GetProperty("summary").GetProperty("polylineCount").GetInt32().Should().Be(2);
        json.GetProperty("summary").GetProperty("countsByLevel").GetArrayLength().Should().Be(2);
        json.GetProperty("geoJson").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Border_geojson_returns_line_features()
    {
        var json = await GetJson("/api/borders?format=geojson&levels=2");

        var features = json.GetProperty("geoJson").GetProperty("features");
        features.GetArrayLength().Should().Be(1);
        features[0]
            .GetProperty("geometry")
            .GetProperty("type")
            .GetString()
            .Should()
            .Be("LineString");
        features[0].GetProperty("adminLevel").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Inverted_bounds_are_rejected()
    {
        var response = await Client.GetAsync("/api/borders?minLon=10&minLat=0&maxLon=1&maxLat=1");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Admin_regions_list_the_seeded_regions()
    {
        var json = await GetJson("/api/admin-regions?levels=2&limit=5");

        json.GetProperty("regions").GetArrayLength().Should().Be(1);
        json.GetProperty("regions")[0].GetProperty("name").GetString().Should().Be("Deutschland");
        json.GetProperty("regions")[0].GetProperty("adminLevel").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Admin_regions_at_returns_the_containing_regions_deepest_first()
    {
        var json = await GetJson("/api/admin-regions/at?lon=3&lat=3&levels=2,4");

        var regions = json.GetProperty("regions");
        regions.GetArrayLength().Should().Be(2);
        regions[0].GetProperty("name").GetString().Should().Be("Bayern");
        regions[1].GetProperty("name").GetString().Should().Be("Deutschland");
        json.GetProperty("longitude").GetDouble().Should().Be(3);
        json.GetProperty("latitude").GetDouble().Should().Be(3);
    }

    [Fact]
    public async Task Admin_region_detail_returns_level_bounds_and_point_count()
    {
        var json = await GetJson("/api/admin-regions/region:de");

        json.GetProperty("id").GetString().Should().Be("region:de");
        json.GetProperty("name").GetString().Should().Be("Deutschland");
        json.GetProperty("adminLevel").GetInt32().Should().Be(2);
        json.GetProperty("pointCount").GetInt32().Should().Be(5);
        json.GetProperty("bounds").GetProperty("minLongitude").GetDouble().Should().Be(0);
    }

    [Fact]
    public async Task Admin_region_missing_returns_404()
    {
        var response = await Client.GetAsync("/api/admin-regions/region:missing");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Admin_region_children_returns_the_contained_regions()
    {
        var json = await GetJson("/api/admin-regions/region:de/children?level=4");

        json.GetProperty("parentId").GetString().Should().Be("region:de");
        json.GetProperty("childLevel").GetInt32().Should().Be(4);
        json.GetProperty("regions").GetArrayLength().Should().Be(1);
        json.GetProperty("regions")[0].GetProperty("name").GetString().Should().Be("Bayern");
    }

    [Fact]
    public async Task Admin_regions_stats_report_counts_and_database_size()
    {
        var json = await GetJson("/api/admin-regions/stats");

        json.GetProperty("regionCount").GetInt32().Should().Be(2);
        json.GetProperty("lineCount").GetInt32().Should().Be(1);
        json.GetProperty("levels")
            .EnumerateArray()
            .Select(level => level.GetInt32())
            .Should()
            .Equal(2, 4);
        json.GetProperty("databaseBytes").GetInt64().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Admin_regions_at_rejects_out_of_range_coordinates()
    {
        var response = await Client.GetAsync("/api/admin-regions/at?lon=999&lat=0&levels=2");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Settings_expose_the_window_size()
    {
        var json = await GetJson("/api/settings");

        json.GetProperty("window").GetProperty("maximized").GetBoolean().Should().BeTrue();
        json.GetProperty("window").GetProperty("width").GetInt32().Should().Be(0);
        json.GetProperty("window").GetProperty("height").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Setting_the_window_without_a_launcher_returns_conflict()
    {
        var response = await Client.PutAsJsonAsync(
            "/api/settings/window",
            new
            {
                maximized = false,
                width = 1600,
                height = 900,
            }
        );

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_screenshot_without_a_launcher_returns_conflict()
    {
        var response = await Client.GetAsync("/api/launcher/screenshot");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task The_map_routes_return_conflict_without_a_launcher()
    {
        (await Client.GetAsync("/api/map")).StatusCode.Should().Be(HttpStatusCode.Conflict);

        (
            await Client.PostAsJsonAsync(
                "/api/map/focus",
                new { longitude = 10.45, latitude = 51.16 }
            )
        )
            .StatusCode.Should()
            .Be(HttpStatusCode.Conflict);

        (await Client.PostAsJsonAsync("/api/map/focus-region", new { regionId = "region:51477" }))
            .StatusCode.Should()
            .Be(HttpStatusCode.Conflict);

        (await Client.PostAsync("/api/map/blur", null))
            .StatusCode.Should()
            .Be(HttpStatusCode.Conflict);

        (await Client.PostAsJsonAsync("/api/map/pan", new { dx = 64, dy = 0 }))
            .StatusCode.Should()
            .Be(HttpStatusCode.Conflict);

        (await Client.PostAsJsonAsync("/api/map/zoom", new { direction = 1 }))
            .StatusCode.Should()
            .Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Swagger_documents_the_border_endpoint()
    {
        var response = await Client.GetAsync("/swagger/v1/swagger.json");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("/api/borders");
    }

    [Fact]
    public async Task Graphql_answers_the_state_query()
    {
        var response = await Client.PostAsJsonAsync(
            "/graphql",
            new
            {
                query = "{ state { launcherStatus local { artifactCount } } regions { polylineCount availableAdminLevels } }",
            }
        );
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("\"launcherStatus\":\"offline\"");
        body.Should().Contain("\"polylineCount\":2");
    }

    private async Task<JsonElement> GetJson(string path)
    {
        var response = await Client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        return document.RootElement.Clone();
    }
}
