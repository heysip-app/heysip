using HeySip.Core.Features.Settings;

namespace HeySip.Tests.Features.Settings;

public sealed class WeekendDaysTests
{
    private static WorkHoursPreset Preset(string id) => WorkHoursPresets.All.Single(p => p.Id == id);

    [Fact]
    public void The_weekend_is_Saturday_and_Sunday_until_changed()
    {
        Assert.Equal([DayOfWeek.Saturday, DayOfWeek.Sunday], new AppSettings().WeekendDays);
        Assert.Equal(5, Preset("weekends-off").Apply(new AppSettings()).ActiveDays.Length);
    }

    [Theory]
    [InlineData(new[] { DayOfWeek.Friday, DayOfWeek.Saturday }, DayOfWeek.Sunday)]
    [InlineData(new[] { DayOfWeek.Sunday }, DayOfWeek.Monday)]
    [InlineData(new[] { DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Saturday }, DayOfWeek.Tuesday)]
    public void Weekend_presets_leave_out_exactly_the_chosen_days(DayOfWeek[] weekend, DayOfWeek stillActive)
    {
        var settings = new AppSettings { WeekendDays = weekend };

        foreach (var id in new[] { "office", "weekends-off" })
        {
            var applied = Preset(id).Apply(settings);
            Assert.Equal(7 - weekend.Length, applied.ActiveDays.Length);
            Assert.Contains(stillActive, applied.ActiveDays);
            Assert.All(weekend, day => Assert.DoesNotContain(day, applied.ActiveDays));
            Assert.True(Preset(id).Matches(applied));
        }
    }

    [Fact]
    public void A_week_with_no_weekend_makes_the_weekend_presets_run_every_day()
    {
        var applied = Preset("weekends-off").Apply(new AppSettings { WeekendDays = [] });

        Assert.Equal(7, applied.ActiveDays.Length);
    }

    [Fact]
    public void Non_weekend_presets_ignore_the_weekend()
    {
        var applied = Preset("early").Apply(new AppSettings { WeekendDays = [DayOfWeek.Monday] });

        Assert.Equal(7, applied.ActiveDays.Length);
    }

    [Fact]
    public void Presets_describe_themselves_for_the_users_own_weekend()
    {
        Assert.Equal("9:00 to 18:00, Monday to Friday", Preset("office").Describe(new AppSettings()));
        Assert.Equal("9:00 to 18:00, Sunday to Thursday", Preset("office").Describe(new AppSettings { WeekendDays = [DayOfWeek.Friday, DayOfWeek.Saturday] }));
        Assert.Equal("10:00 to 23:00, all week", Preset("weekends-off").Describe(new AppSettings { WeekendDays = [] }));
        Assert.Equal("10:00 to 23:00, all week", Preset("everyday").Describe(new AppSettings()));
    }

    [Theory]
    [InlineData(new DayOfWeek[0], "no days")]
    [InlineData(new[] { DayOfWeek.Friday }, "Friday")]
    [InlineData(new[] { DayOfWeek.Friday, DayOfWeek.Saturday }, "Friday, Saturday")]
    [InlineData(new[] { DayOfWeek.Monday, DayOfWeek.Wednesday }, "Monday, Wednesday")]
    [InlineData(new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday }, "Monday to Wednesday")]
    [InlineData(new[] { DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday }, "Sunday to Thursday")]
    public void Day_lists_read_like_a_person_would_say_them(DayOfWeek[] days, string expected)
    {
        Assert.Equal(expected, DayListText.Describe(days));
    }

    [Fact]
    public void The_weekend_is_normalised_and_an_empty_one_is_kept()
    {
        var tidied = new AppSettings { WeekendDays = [DayOfWeek.Sunday, DayOfWeek.Friday, DayOfWeek.Friday] }.Normalize();
        Assert.Equal([DayOfWeek.Sunday, DayOfWeek.Friday], tidied.WeekendDays);

        Assert.Empty(new AppSettings { WeekendDays = [] }.Normalize().WeekendDays);
    }

    [Fact]
    public void Changing_only_the_weekend_counts_as_a_change()
    {
        var before = new AppSettings();

        Assert.False(before.SameAs(before with { WeekendDays = [DayOfWeek.Friday] }));
        Assert.True(before.SameAs(before with { WeekendDays = [DayOfWeek.Saturday, DayOfWeek.Sunday] }));
    }
}
