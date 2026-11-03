using Avalonia.Controls;

namespace HeySip.App.Features.Companion;

public partial class CompanionWindow : Window
{
    public CompanionWindow()
    {
        InitializeComponent();
        // Clicking the buddy or its bubble (not the transparent rest of the window, nor the answer buttons, which
        // handle their own clicks) opens HeySip.
        void OpenOnClick(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            if (!e.Handled && DataContext is CompanionViewModel viewModel)
            {
                viewModel.RequestOpen();
            }
        }

        Bubble.PointerPressed += OpenOnClick;
        Pet.PointerPressed += OpenOnClick;
        DataContextChanged += (_, _) =>
        {
            if (DataContext is CompanionViewModel viewModel)
            {
                viewModel.ConfettiRequested += () => Confetti.Burst();
            }
        };
    }
}
