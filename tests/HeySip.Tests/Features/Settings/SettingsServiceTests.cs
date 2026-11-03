using HeySip.Core.Features.Companion;
using HeySip.Core.Features.Settings;
using HeySip.Tests.Support;

namespace HeySip.Tests.Features.Settings;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly TempData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void Defaults_are_used_when_there_is_no_file()
    {
        var settings = new SettingsService(_data.Location);
        settings.Load();

        Assert.Equal(45, settings.Current.IntervalMinutes);
        Assert.Equal(new TimeOnly(10, 0), settings.Current.ActiveStart);
        Assert.Equal(7, settings.Current.ActiveDays.Length);
        Assert.False(settings.Current.Onboarded);
    }

    [Fact]
    public void Changes_are_saved_and_reloaded()
    {
        var settings = new SettingsService(_data.Location);
        settings.Load();
        settings.Update(s => s with
        {
            Name = "Alex",
            IntervalMinutes = 30,
            Character = "heysip.minty",
            Messages = MessageStyle.Coach,
            ActiveStart = new TimeOnly(8, 30),
            ActiveDays = [DayOfWeek.Monday, DayOfWeek.Friday],
            TimeZoneId = "UTC",
        });

        var reloaded = new SettingsService(_data.Location);
        reloaded.Load();

        Assert.Equal("Alex", reloaded.Current.Name);
        Assert.Equal(30, reloaded.Current.IntervalMinutes);
        Assert.Equal("heysip.minty", reloaded.Current.Character);
        Assert.Equal(MessageStyle.Coach, reloaded.Current.Messages);
        Assert.Equal(new TimeOnly(8, 30), reloaded.Current.ActiveStart);
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Friday], reloaded.Current.ActiveDays);
        Assert.Equal("UTC", reloaded.Current.TimeZoneId);
    }

    [Fact]
    public void Update_raises_Changed_only_when_something_changed()
    {
        var settings = new SettingsService(_data.Location);
        settings.Load();
        var raised = 0;
        settings.Changed += (_, _) => raised++;

        settings.Update(s => s with { Name = "Sam" });
        settings.Update(s => s with { Name = "Sam" });

        Assert.Equal(1, raised);
    }

    [Fact]
    public void Out_of_range_values_are_clamped()
    {
        var settings = new SettingsService(_data.Location);
        settings.Load();

        settings.Update(s => s with
        {
            IntervalMinutes = 0,
            SnoozeMinutes = 9999,
            DailyGoalMilliliters = 5,
            Name = new string('x', 80),
            ActiveDays = [],
        });

        Assert.Equal(1, settings.Current.IntervalMinutes);
        Assert.Equal(120, settings.Current.SnoozeMinutes);
        Assert.Equal(250, settings.Current.DailyGoalMilliliters);
        Assert.Equal(24, settings.Current.Name.Length);
        Assert.Empty(settings.Current.ActiveDays); // an empty choice is kept: it means no reminders on any day
    }

    [Fact]
    public void A_corrupt_file_falls_back_to_defaults_instead_of_crashing()
    {
        File.WriteAllText(_data.Location.SettingsPath, "{ not json");
        var settings = new SettingsService(_data.Location);

        settings.Load();

        Assert.Equal(45, settings.Current.IntervalMinutes);
    }

    [Fact]
    public void An_unreadable_file_is_kept_as_a_backup_and_the_problem_is_logged()
    {
        File.WriteAllText(_data.Location.SettingsPath, "{ \"Name\": \"Precious\", broken");
        var log = new HeySip.Core.SharedKernel.Diagnostics.DiagnosticLog(TimeProvider.System, _data.Location.LogDirectory);
        var settings = new SettingsService(_data.Location, log);

        settings.Load();
        settings.Update(s => s with { Name = "New" }); // saving over the file must not destroy the original

        Assert.Contains("Precious", File.ReadAllText(_data.Location.SettingsPath + ".bad"));
        Assert.Contains(log.Tail(), line => line.Contains("settings.json couldn't be read"));
    }

    [Fact]
    public void A_null_day_list_in_the_file_means_every_day_but_an_empty_one_is_kept()
    {
        File.WriteAllText(_data.Location.SettingsPath, "{ \"ActiveDays\": null }");
        var settings = new SettingsService(_data.Location);
        settings.Load();
        Assert.Equal(7, settings.Current.ActiveDays.Length);

        File.WriteAllText(_data.Location.SettingsPath, "{ \"ActiveDays\": [] }");
        settings.Load();
        Assert.Empty(settings.Current.ActiveDays);
    }

    [Fact]
    public void A_settings_file_that_cannot_be_written_still_applies_for_the_session_and_is_reported()
    {
        Directory.CreateDirectory(_data.Location.SettingsPath); // a folder where the file should be, so saving fails
        var log = new HeySip.Core.SharedKernel.Diagnostics.DiagnosticLog(TimeProvider.System, _data.Location.LogDirectory);
        var settings = new SettingsService(_data.Location, log);
        settings.Load();
        var raised = 0;
        settings.Changed += (_, _) => raised++;

        var failure = Record.Exception(() => settings.Update(s => s with { Name = "Sam" }));

        Assert.Null(failure);
        Assert.Equal("Sam", settings.Current.Name);
        Assert.Equal(1, raised); // the rest of the app still hears about the change
        Assert.NotNull(settings.LastSaveError);
        Assert.Contains(log.Tail(), line => line.Contains("Couldn't save settings.json"));
    }

    [Fact]
    public void A_settings_file_from_before_the_log_panel_was_removed_still_loads()
    {
        File.WriteAllText(_data.Location.SettingsPath, "{ \"Name\": \"Kai\", \"ShowLogPanel\": false, \"IntervalMinutes\": 30 }");
        var settings = new SettingsService(_data.Location);

        settings.Load();

        Assert.Equal("Kai", settings.Current.Name);
        Assert.Equal(30, settings.Current.IntervalMinutes);
        Assert.Null(settings.LastSaveError);
        Assert.False(File.Exists(_data.Location.SettingsPath + ".bad"));
    }
}
