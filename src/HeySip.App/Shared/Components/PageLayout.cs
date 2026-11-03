using Avalonia;
using Avalonia.Controls;

namespace HeySip.App.Shared.Components;

/// <summary>
/// The frame every page shares: a scrolling column with a title (and optional controls beside it), then the page's
/// cards stacked below. Putting this in one place keeps margins, width and spacing identical across pages.
/// </summary>
public class PageLayout : ItemsControl
{
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<PageLayout, string?>(nameof(Title));

    public static readonly StyledProperty<object?> ActionsProperty = AvaloniaProperty.Register<PageLayout, object?>(nameof(Actions));

    /// <summary>The page's scroll viewer, once its template has been applied.</summary>
    public ScrollViewer? Scroller { get; private set; }

    /// <summary>Raised when the page scrolls or its content changes size, so a list can load more as the end comes near.</summary>
    public event EventHandler<ScrollChangedEventArgs>? ScrollChanged;

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Controls shown on the right of the title, such as a range switch.</summary>
    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }

    protected override void OnApplyTemplate(Avalonia.Controls.Primitives.TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (Scroller is not null)
        {
            Scroller.ScrollChanged -= OnScrollChanged;
        }

        Scroller = e.NameScope.Find<ScrollViewer>("PART_Scroll");
        if (Scroller is not null)
        {
            Scroller.ScrollChanged += OnScrollChanged;
        }
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e) => ScrollChanged?.Invoke(this, e);
}
