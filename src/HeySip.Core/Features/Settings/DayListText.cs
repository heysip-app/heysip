namespace HeySip.Core.Features.Settings;

/// <summary>Writes a set of weekdays the way a person would say it: "all week", "Monday to Friday", "Sunday to Thursday", "Friday, Saturday".</summary>
public static class DayListText
{
    public static string Describe(IEnumerable<DayOfWeek> days)
    {
        var week = Enum.GetValues<DayOfWeek>().OrderBy(day => ((int)day + 6) % 7).ToList(); // Monday first
        var set = days.ToHashSet();
        var chosen = week.Where(set.Contains).ToList();

        if (chosen.Count == 0)
        {
            return "no days";
        }

        if (chosen.Count == week.Count)
        {
            return "all week";
        }

        // Three or more days in a row read better as a range. The week is a circle, so Sunday to Thursday counts.
        if (chosen.Count >= 3)
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
                    return $"{week[start]} to {week[(start + length - 1) % week.Count]}";
                }
            }
        }

        return string.Join(", ", chosen);
    }
}
