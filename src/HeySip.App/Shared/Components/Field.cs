using Avalonia;
using Avalonia.Controls;

namespace HeySip.App.Shared.Components;

/// <summary>A small caption above a single input, the way every settings and form row is laid out.</summary>
public class Field : ContentControl
{
    public static readonly StyledProperty<string?> LabelProperty = AvaloniaProperty.Register<Field, string?>(nameof(Label));

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }
}
