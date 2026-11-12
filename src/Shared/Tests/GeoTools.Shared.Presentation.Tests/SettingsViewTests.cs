using FluentAssertions;
using GeoTools.Shared.Application.Scenes;
using GeoTools.Shared.Application.Views;
using GeoTools.Shared.Presentation.Options;
using GeoTools.Shared.Presentation.Views;

namespace GeoTools.Shared.Presentation.Tests;

public sealed class SettingsViewTests
{
    [Fact]
    public void Id_is_settings()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new WindowOptions());

        new SettingsView(options).Id.Should().Be(ViewId.Settings);
    }

    [Fact]
    public void Compose_labels_the_configured_window()
    {
        var options = Microsoft.Extensions.Options.Options.Create(
            new WindowOptions
            {
                Title = "GeoTools (development)",
                Width = 1200,
                Height = 800,
            }
        );

        var scene = new SettingsView(options).Compose(new ViewSize(800, 600));
        var texts = scene.Shapes.OfType<TextDto>().Select(text => text.Text).ToArray();

        texts.Should().Contain("GeoTools (development)");
        texts.Should().Contain("1200 x 800");
    }
}
