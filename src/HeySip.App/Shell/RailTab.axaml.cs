using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;

namespace HeySip.App.Shell;

public partial class RailTab : UserControl
{
    private TabViewModel? _tab;

    public RailTab()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Watch(DataContext as TabViewModel);
    }

    private void Watch(TabViewModel? tab)
    {
        if (_tab is not null)
        {
            _tab.PropertyChanged -= OnTabChanged;
        }

        _tab = tab;
        if (_tab is not null)
        {
            _tab.PropertyChanged += OnTabChanged;
            ScrollIntoViewIfSelected();
        }
    }

    private void OnTabChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TabViewModel.IsSelected))
        {
            ScrollIntoViewIfSelected();
        }
    }

    /// <summary>Opening a day or cycling with Ctrl+Tab must never select a tab that is scrolled out of sight.</summary>
    private void ScrollIntoViewIfSelected()
    {
        if (_tab is { IsSelected: true })
        {
            Dispatcher.UIThread.Post(
                () => RaiseEvent(new RequestBringIntoViewEventArgs
                {
                    RoutedEvent = RequestBringIntoViewEvent,
                    TargetObject = this,
                    TargetRect = new Avalonia.Rect(Bounds.Size),
                }),
                DispatcherPriority.Loaded);
        }
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Watch(DataContext as TabViewModel);
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Watch(null);
    }
}
