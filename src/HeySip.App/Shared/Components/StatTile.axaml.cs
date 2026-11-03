using Avalonia;
using Avalonia.Controls;

namespace HeySip.App.Shared.Components;

/// <summary>A small card showing one number under a caption.</summary>
public partial class StatTile : UserControl
{
    public static readonly StyledProperty<string?> LabelProperty = AvaloniaProperty.Register<StatTile, string?>(nameof(Label));

    public static readonly StyledProperty<string?> ValueProperty = AvaloniaProperty.Register<StatTile, string?>(nameof(Value));

    public StatTile() => InitializeComponent();

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }
}
