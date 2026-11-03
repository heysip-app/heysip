using Avalonia.Controls;

namespace HeySip.App.Features.History;

public partial class HistoryView : UserControl
{
    /// <summary>How close to the bottom (in pixels) the list may get before the next days are loaded.</summary>
    private const double LoadAhead = 320;

    public HistoryView()
    {
        InitializeComponent();
        Page.ScrollChanged += (_, _) => LoadMoreIfNearEnd();
    }

    /// <summary>
    /// Scrolling near the end loads the next days. The same check runs when the content changes size, so a tall window
    /// that the first page doesn't fill keeps loading until it is full (or there is nothing left).
    /// </summary>
    private void LoadMoreIfNearEnd()
    {
        if (DataContext is not HistoryViewModel { HasMore: true } history || Page.Scroller is not { } scroller)
        {
            return;
        }

        var remaining = scroller.Extent.Height - (scroller.Offset.Y + scroller.Viewport.Height);
        if (remaining < LoadAhead)
        {
            history.LoadMore();
        }
    }
}
