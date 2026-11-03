using Avalonia;
using Avalonia.Controls;

namespace HeySip.App.Features.Settings;

/// <summary>Shows one <see cref="CharacterOptionViewModel"/> as a selectable tile.</summary>
public partial class CharacterPick : UserControl
{
    private const double MascotAspect = 1.4;

    public static readonly StyledProperty<double> PreviewWidthProperty = AvaloniaProperty.Register<CharacterPick, double>(nameof(PreviewWidth), 70);

    public static readonly StyledProperty<double> PreviewHeightProperty = AvaloniaProperty.Register<CharacterPick, double>(nameof(PreviewHeight), 98);

    static CharacterPick() =>
        PreviewWidthProperty.Changed.AddClassHandler<CharacterPick>((pick, e) => pick.PreviewHeight = Math.Round((double)e.NewValue! * MascotAspect));

    public CharacterPick() => InitializeComponent();

    public double PreviewWidth
    {
        get => GetValue(PreviewWidthProperty);
        set => SetValue(PreviewWidthProperty, value);
    }

    public double PreviewHeight
    {
        get => GetValue(PreviewHeightProperty);
        private set => SetValue(PreviewHeightProperty, value);
    }
}
