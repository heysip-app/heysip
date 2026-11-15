using System.Globalization;
using HeySip.Core.SharedKernel;

namespace HeySip.Tests.SharedKernel;

/// <summary>People type times in many ways; each of them must come out as the time they meant, and nothing else should.</summary>
public sealed class TimeInputTests
{
    private static readonly CultureInfo UnitedStates = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo Britain = CultureInfo.GetCultureInfo("en-GB");

    [Theory]
    [InlineData("9", 9, 0)]
    [InlineData("9:30", 9, 30)]
    [InlineData("9.30", 9, 30)]
    [InlineData("930", 9, 30)]
    [InlineData("0930", 9, 30)]
    [InlineData("2130", 21, 30)]
    [InlineData("21", 21, 0)]
    [InlineData("21:15", 21, 15)]
    [InlineData("0", 0, 0)]
    [InlineData("00:30", 0, 30)]
    [InlineData("9pm", 21, 0)]
    [InlineData("9 PM", 21, 0)]
    [InlineData("9:30 p.m.", 21, 30)]
    [InlineData("7a", 7, 0)]
    [InlineData("  7:05 am  ", 7, 5)]
    [InlineData("12am", 0, 0)]
    [InlineData("12pm", 12, 0)]
    [InlineData("12", 12, 0)]
    [InlineData("noon", 12, 0)]
    [InlineData("Midnight", 0, 0)]
    public void A_typed_time_is_read_as_meant(string text, int hour, int minute)
    {
        Assert.True(TimeInput.TryParse(text, previous: null, out var time, UnitedStates));
        Assert.Equal(new TimeOnly(hour, minute), time);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("banana")]
    [InlineData("pm")]
    [InlineData("25")]
    [InlineData("99")]
    [InlineData("24:00")]
    [InlineData("9:60")]
    [InlineData("9:5")]
    [InlineData("13pm")]
    [InlineData("0pm")]
    [InlineData("2500")]
    public void Something_that_is_not_a_time_is_refused(string? text)
    {
        Assert.False(TimeInput.TryParse(text, previous: null, out _, UnitedStates));
    }

    [Theory]
    [InlineData("9", 21, 0)]       // an evening time is showing, so 9 means 9 in the evening
    [InlineData("10", 22, 0)]
    [InlineData("9:45", 21, 45)]
    [InlineData("09", 9, 0)]       // a leading zero is always the morning
    [InlineData("9am", 9, 0)]      // saying am or pm always wins
    [InlineData("12", 12, 0)]      // twelve is noon
    [InlineData("14", 14, 0)]      // the afternoon on a 24-hour clock stays as typed
    public void A_bare_hour_keeps_the_half_of_the_day_that_was_showing(string text, int hour, int minute)
    {
        Assert.True(TimeInput.TryParse(text, new TimeOnly(22, 0), out var time, UnitedStates));
        Assert.Equal(new TimeOnly(hour, minute), time);
    }

    [Fact]
    public void A_bare_hour_stays_in_the_morning_when_a_morning_time_was_showing()
    {
        Assert.True(TimeInput.TryParse("9", new TimeOnly(10, 0), out var time, UnitedStates));
        Assert.Equal(new TimeOnly(9, 0), time);
    }

    [Theory]
    [InlineData("9", 9, 0)]        // on a 24-hour clock a bare hour is just that hour, even with an evening time showing
    [InlineData("10", 10, 0)]
    [InlineData("9:45", 9, 45)]
    [InlineData("21", 21, 0)]
    [InlineData("9pm", 21, 0)]
    public void On_a_24_hour_clock_a_bare_hour_does_not_jump_to_the_evening(string text, int hour, int minute)
    {
        Assert.True(TimeInput.TryParse(text, new TimeOnly(22, 0), out var time, Britain));
        Assert.Equal(new TimeOnly(hour, minute), time);
    }

    [Fact]
    public void A_time_written_for_the_screen_can_be_typed_back_in_for_every_language()
    {
        var failures = new List<string>();
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            for (var minutes = 0; minutes < 24 * 60; minutes += 5)
            {
                var time = TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(minutes));
                var written = TimeInput.Format(time, culture);

                if (!TimeInput.TryParse(written, time, out var back, culture) || back != time)
                {
                    failures.Add($"{culture.Name} '{written}'");
                    break;
                }
            }
        }

        Assert.True(failures.Count == 0, "Cannot be typed back in: " + string.Join(", ", failures.Take(10)));
    }
}
