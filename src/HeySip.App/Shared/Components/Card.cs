using Avalonia;
using Avalonia.Controls;

namespace HeySip.App.Shared.Components;

/// <summary>A raised card with an optional heading. Whatever is put inside is stacked with <see cref="Spacing"/> between items.</summary>
public class Card : ItemsControl
{
    public static readonly StyledProperty<string?> HeadingProperty = AvaloniaProperty.Register<Card, string?>(nameof(Heading));

    public static readonly StyledProperty<double> SpacingProperty = AvaloniaProperty.Register<Card, double>(nameof(Spacing), 12);

    public string? Heading
    {
        get => GetValue(HeadingProperty);
        set => SetValue(HeadingProperty, value);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }
}
