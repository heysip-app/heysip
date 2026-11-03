using HeySip.Core.Features.Hydration;
using HeySip.Tests.Support;

namespace HeySip.Tests.Features.Hydration;

public sealed class HistoryPagingTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 3, 10); // the harness clock is noon UTC on this day

    private readonly CoreHarness _core = new();

    public void Dispose() => _core.Dispose();

    private void Drink(int daysAgo, int hour = 9, int ml = 250, int minute = 0) =>
        _core.Store.Add(new DateTimeOffset(Today.AddDays(-daysAgo).ToDateTime(new TimeOnly(hour, minute)), TimeSpan.Zero), ml);

    private List<HistoryPage> ReadEverything(int pageSize)
    {
        var pages = new List<HistoryPage>();
        DateOnly? cursor = null;
        do
        {
            var page = _core.Hydration.BrowseHistory(cursor, pageSize);
            pages.Add(page);
            cursor = page.Next;
        }
        while (cursor is not null && pages.Count < 1000);

        return pages;
    }

    [Fact]
    public void An_empty_history_has_no_days_and_nothing_more()
    {
        var page = _core.Hydration.BrowseHistory(null);

        Assert.Empty(page.Days);
        Assert.Null(page.Next);
    }

    [Fact]
    public void Days_without_drinks_are_skipped_and_the_rest_are_newest_first()
    {
        Drink(0);
        Drink(3);
        Drink(10);

        var page = _core.Hydration.BrowseHistory(null);

        Assert.Equal([Today, Today.AddDays(-3), Today.AddDays(-10)], page.Days.Select(day => day.Date));
        Assert.Null(page.Next);
    }

    [Fact]
    public void Today_is_left_out_when_nothing_has_been_drunk_yet()
    {
        Drink(2);

        var page = _core.Hydration.BrowseHistory(null);

        Assert.Equal([Today.AddDays(-2)], page.Days.Select(day => day.Date));
    }

    [Fact]
    public void A_day_carries_its_total_goal_mark_and_entries_oldest_first()
    {
        Drink(1, hour: 18, ml: 1500);
        Drink(1, hour: 8, ml: 600);

        var day = _core.Hydration.BrowseHistory(null).Days.Single();

        Assert.Equal(2100, day.Milliliters);
        Assert.True(day.GoalMet);
        Assert.Equal([600, 1500], day.Entries.Select(entry => entry.Milliliters));
    }

    [Fact]
    public void Paging_walks_back_through_every_day_exactly_once()
    {
        for (var i = 0; i < 30; i++)
        {
            Drink(i);
        }

        var pages = ReadEverything(pageSize: 14);

        Assert.Equal([14, 14, 2], pages.Select(page => page.Days.Count));
        Assert.Equal(30, pages.SelectMany(page => page.Days).Select(day => day.Date).Distinct().Count());
        Assert.Equal(
            Enumerable.Range(0, 30).Select(i => Today.AddDays(-i)),
            pages.SelectMany(page => page.Days).Select(day => day.Date));
        Assert.Null(pages[^1].Next);
    }

    [Fact]
    public void A_page_that_ends_exactly_at_the_last_day_says_there_is_nothing_more()
    {
        for (var i = 0; i < 14; i++)
        {
            Drink(i);
        }

        var page = _core.Hydration.BrowseHistory(null, 14);

        Assert.Equal(14, page.Days.Count);
        Assert.Null(page.Next);
    }

    [Fact]
    public void A_day_is_never_split_between_two_pages()
    {
        for (var day = 0; day < 10; day++)
        {
            for (var n = 0; n < 20; n++)
            {
                Drink(day, hour: 6 + (n % 12), minute: n);
            }
        }

        var pages = ReadEverything(pageSize: 3); // 36 entries fit per look, but a day alone is 20

        Assert.All(pages.SelectMany(page => page.Days), day => Assert.Equal(20, day.Entries.Count));
        Assert.Equal(10, pages.SelectMany(page => page.Days).Select(day => day.Date).Distinct().Count());
    }

    [Fact]
    public void One_enormous_day_is_still_returned_whole()
    {
        for (var n = 0; n < 100; n++)
        {
            Drink(0, hour: 0, minute: n % 60, ml: 10);
        }

        Drink(5);

        var pages = ReadEverything(pageSize: 1);

        Assert.Equal([Today, Today.AddDays(-5)], pages.SelectMany(page => page.Days).Select(day => day.Date));
        Assert.Equal(100, pages[0].Days[0].Entries.Count);
    }

    [Fact]
    public void Continuing_from_a_day_returns_only_earlier_days()
    {
        Drink(0);
        Drink(1);
        Drink(2);

        var page = _core.Hydration.BrowseHistory(Today.AddDays(-1));

        Assert.Equal([Today.AddDays(-2)], page.Days.Select(day => day.Date));
    }
}
