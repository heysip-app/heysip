namespace HeySip.Core.Features.Settings;

/// <summary>Writes a set of weekdays the way a person would say it: "all week", "Monday to Friday", "Sunday to Thursday", "Friday, Saturday".</summary>
public static class DayListText
{
    private const string NoDays = "no days";
    private const string AllWeek = "all week";
    private const string RangeSeparator = " to ";
    private const string ListSeparator = ", ";

    /// <summary>Fewer days in a row than this read better as a list ("Friday, Saturday") than as a range.</summary>
    private const int MinDaysForRange = 3;

    public static string Describe(IEnumerable<DayOfWeek> days)
    {
        var week = WeekDays.MondayFirst;
        var set = days.ToHashSet();
        var chosen = week.Where(set.Contains).ToList();

        if (chosen.Count == 0)
        {
            return NoDays;
        }

        if (chosen.Count == week.Count)
        {
            return AllWeek;
        }

        // Three or more days in a row read better as a range. The week is a circle, so Sunday to Thursday counts.
        if (chosen.Count >= MinDaysForRange)
        {
            for (var start = 0; start < week.Count; start++)
            {
                var previous = week[(start + week.Count - 1) % week.Count];
                if (!set.Contains(week[start]) || set.Contains(previous))
                {
                    continue;
                }

                var length = 0;
                while (length < week.Count && set.Contains(week[(start + length) % week.Count]))
                {
                    length++;
                }

                if (length == chosen.Count)
                {
                    return $"{week[start]}{RangeSeparator}{week[(start + length - 1) % week.Count]}";
                }
            }
        }

        return string.Join(ListSeparator, chosen);
    }
}
