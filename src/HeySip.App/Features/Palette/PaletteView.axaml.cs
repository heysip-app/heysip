using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace HeySip.App.Features.Palette;

public partial class PaletteView : UserControl
{
    public PaletteView()
    {
        InitializeComponent();

        // Tunnel the keys so the arrow keys move the highlighted row instead of the text caret.
        Input.AddHandler(KeyDownEvent, OnInputKeyDown, RoutingStrategies.Tunnel);
        Backdrop.PointerPressed += (_, e) =>
        {
            if (e.Source == Backdrop && DataContext is CommandPaletteViewModel viewModel)
            {
                viewModel.Close();
            }
        };

        DataContextChanged += (_, _) =>
        {
            if (DataContext is CommandPaletteViewModel viewModel)
            {
                viewModel.SelectionMoved += ScrollIntoView;
            }
        };
    }

    protected override void OnPropertyChanged(Avalonia.AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // When the palette appears, put the cursor in the box ready to type.
        if (change.Property == IsVisibleProperty && change.NewValue is true)
        {
            Dispatcher.UIThread.Post(() =>
            {
                Input.Focus();
                Input.SelectAll();
            });
        }
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not CommandPaletteViewModel viewModel)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Down:
                viewModel.MoveNext();
                break;
            case Key.Up:
                viewModel.MovePrevious();
                break;
            case Key.Enter:
                viewModel.RunSelected();
                break;
            case Key.Escape:
                viewModel.Close();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void ScrollIntoView(int index)
    {
        Dispatcher.UIThread.Post(() => ResultsList.ContainerFromIndex(index)?.BringIntoView());
    }
}
