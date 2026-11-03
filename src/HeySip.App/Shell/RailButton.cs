using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace HeySip.App.Shell;

/// <summary>An icon button for the left rail. <see cref="IsCurrent"/> marks the page being shown; <see cref="IsOn"/> a panel that is open.</summary>
public class RailButton : Button
{
    public static readonly StyledProperty<Geometry?> IconProperty = AvaloniaProperty.Register<RailButton, Geometry?>(nameof(Icon));

    public static readonly StyledProperty<bool> IsCurrentProperty = AvaloniaProperty.Register<RailButton, bool>(nameof(IsCurrent));

    public static readonly StyledProperty<bool> IsOnProperty = AvaloniaProperty.Register<RailButton, bool>(nameof(IsOn));

    static RailButton()
    {
        IconProperty.Changed.AddClassHandler<RailButton>((button, e) => button.Content = new PathIcon { Data = e.NewValue as Geometry });
        IsCurrentProperty.Changed.AddClassHandler<RailButton>((button, e) => button.Classes.Set("selected", (bool)e.NewValue!));
        IsOnProperty.Changed.AddClassHandler<RailButton>((button, e) => button.Classes.Set("on", (bool)e.NewValue!));
    }

    public RailButton() => Classes.Add("rail");

    protected override Type StyleKeyOverride => typeof(Button);

    public Geometry? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public bool IsCurrent
    {
        get => GetValue(IsCurrentProperty);
        set => SetValue(IsCurrentProperty, value);
    }

    public bool IsOn
    {
        get => GetValue(IsOnProperty);
        set => SetValue(IsOnProperty, value);
    }
}
