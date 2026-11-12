using FluentAssertions;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Domain.Geospatial;
using GeoTools.Shared.Infrastructure.Persistence;

namespace GeoTools.Shared.Infrastructure.Tests;

public sealed class StoreMappingTests
{
    [Fact]
    public void Admin_levels_round_trip_and_reject_junk()
    {
        StoreMappings.FormatLevels([4, 2, 4]).Should().Be("2,4");
        StoreMappings.ParseLevels("2,4").Should().Equal(2, 4);
        StoreMappings.ParseLevels("").Should().BeEmpty();
        var act = () => StoreMappings.ParseLevels("2,x");
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Bounds_round_trip_and_reject_junk()
    {
        var bounds = new GeoBounds(-1.5, -2.5, 3.5, 4.5);
        var text = StoreMappings.FormatBounds(bounds);

        StoreMappings.ParseBounds(text).Should().Be(bounds);
        StoreMappings.ParseBounds(null).Should().BeNull();

        var act = () => StoreMappings.ParseBounds("1,2");
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Enums_round_trip_and_reject_junk()
    {
        StoreMappings.FormatEnum(RunStatus.Succeeded).Should().Be("Succeeded");
        StoreMappings.ParseEnum<RunStatus>("Succeeded").Should().Be(RunStatus.Succeeded);
        var act = () => StoreMappings.ParseEnum<RunStatus>("nope");
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Timestamps_round_trip()
    {
        var value = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);

        StoreMappings
            .FromUnixMilliseconds(StoreMappings.ToUnixMilliseconds(value))
            .Should()
            .Be(value);
    }
}
