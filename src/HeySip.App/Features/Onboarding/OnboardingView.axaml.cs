using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace HeySip.App.Features.Onboarding;

public partial class OnboardingView : UserControl
{
    public static readonly IValueConverter NextLabel =
        new FuncValueConverter<bool, string>(isLast => isLast ? "Let's go!" : "Next");

    public OnboardingView() => InitializeComponent();
}
