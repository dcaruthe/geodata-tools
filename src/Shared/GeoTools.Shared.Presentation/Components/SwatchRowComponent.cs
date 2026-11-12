using GeoTools.Shared.Application.Scenes;
using GeoTools.Shared.Domain.Primatives;
using GeoTools.Shared.Presentation.Views;

namespace GeoTools.Shared.Presentation.Components;

internal sealed class SwatchRowComponent : IViewComponent
{
    private const float Radius = 12;
    private const float Spacing = 36;
    private const float BottomMargin = 60;

    private static readonly Color[] Palette =
    [
        new(0, 90, 200),
        new(0, 180, 90),
        new(255, 220, 0),
        new(220, 40, 40),
        new(40, 90, 240),
        new(240, 40, 200),
    ];

    public void Compose(ViewComposition composition)
    {
        var size = composition.Size;
        var startX = size.Width / 2f - (Palette.Length - 1) * Spacing / 2f;

        for (var index = 0; index < Palette.Length; index++)
            composition.Add(
                new CircleDto(
                    new Point(startX + index * Spacing, size.Height - BottomMargin),
                    Radius,
                    Palette[index],
                    1
                )
            );
    }
}
