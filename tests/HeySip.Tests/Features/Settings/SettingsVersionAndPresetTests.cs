using HeySip.Core.Features.Scheduling;
using HeySip.Core.Features.Settings;
using HeySip.Tests.Support;

namespace HeySip.Tests.Features.Settings;

public sealed class SettingsVersionAndPresetTests : IDisposable
{
    private readonly TempData _data = new();

    public void Dispose() => _data.Dispose();

    private string SettingsFile => _data.Location.SettingsPath;

    // ---- versioning

    [Fact]
    public void Every_migration_step_has_a_matching_current_version()
    {
        var steps = SettingsMigrations.Steps.Count;
        Assert.Equal(AppSettings.CurrentSchemaVersion, steps);
    }

    [Fact]
    public void A_new_settings_file_is_written_with_the_current_version()
    {
        var settings = new SettingsService(_data.Location);
        settings.Load();

        settings.Update(s => s with { Name = "Kai" });

        Assert.Contains($"\"SchemaVersion\": {AppSettings.CurrentSchemaVersion}", File.ReadAllText(SettingsFile));
    }

    [Fact]
    public void A_file_from_before_versions_existed_is_kept_as_a_backup_and_stamped()
    {
        File.WriteAllText(SettingsFile, "{ \"Name\": \"Old\", \"IntervalMinutes\": 20 }");
        var settings = new SettingsService(_data.Location);

        settings.Load();

        Assert.Equal("Old", settings.Current.Name);
        Assert.Equal(20, settings.Current.IntervalMinutes);
        Assert.Equal(AppSettings.CurrentSchemaVersion, settings.Current.SchemaVersion);
        Assert.Contains($"\"SchemaVersion\": {AppSettings.CurrentSchemaVersion}", File.ReadAllText(SettingsFile));
        Assert.Equal("{ \"Name\": \"Old\", \"IntervalMinutes\": 20 }", File.ReadAllText(SettingsFile + ".v0.bak"));
    }

    [Fact]
    public void A_current_file_is_loaded_without_a_backup_or_a_rewrite()
    {
        var first = new SettingsService(_data.Location);
        first.Load();
        first.Update(s => s with { Name = "Kai" });
        var written = File.ReadAllText(SettingsFile);
        var stamp = File.GetLastWriteTimeUtc(SettingsFile);

        new SettingsService(_data.Location).Load();

        Assert.Equal(written, File.ReadAllText(SettingsFile));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(SettingsFile));
        Assert.False(File.Exists(SettingsFile + ".v1.bak"));
    }

    [Fact]
    public void A_file_from_a_newer_version_still_loads_and_is_kept_aside()
    {
        File.WriteAllText(SettingsFile, "{ \"SchemaVersion\": 99, \"Name\": \"Future\", \"SomethingNew\": true }");
        var settings = new SettingsService(_data.Location);

        settings.Load();

        Assert.Equal("Future", settings.Current.Name);
        Assert.True(File.Exists(SettingsFile + ".v99.bak"));
        Assert.Contains("SomethingNew", File.ReadAllText(SettingsFile + ".v99.bak"));
    }

    [Fact]
    public void An_existing_backup_is_never_overwritten()
    {
        File.WriteAllText(SettingsFile + ".v0.bak", "the first one");
        File.WriteAllText(SettingsFile, "{ \"Name\": \"Old\" }");

        new SettingsService(_data.Location).Load();

        Assert.Equal("the first one", File.ReadAllText(SettingsFile + ".v0.bak"));
    }

    [Fact]
    public void Migration_steps_run_in_order_from_the_files_version()
    {
        var trail = new List<string>();
        Func<AppSettings, AppSettings>[] steps =
        [
            s => { trail.Add("0>1"); return s with { Name = s.Name + "a" }; },
            s => { trail.Add("1>2"); return s with { Name = s.Name + "b" }; },
            s => { trail.Add("2>3"); return s with { Name = s.Name + "c" }; },
        ];

        var upgraded = SettingsMigrations.Upgrade(new AppSettings { Name = "x" }, 1, steps);

        Assert.Equal(["1>2", "2>3"], trail);
        Assert.Equal("xbc", upgraded.Name);
    }

    // ---- presets

    [Fact]
    public void Presets_have_unique_ids_and_names_and_sensible_hours()
    {
        Assert.Equal(WorkHoursPresets.All.Count, WorkHoursPresets.All.Select(p => p.Id).Distinct().Count());
        Assert.Equal(WorkHoursPresets.All.Count, WorkHoursPresets.All.Select(p => p.Name).Distinct().Count());
        Assert.All(WorkHoursPresets.All, preset =>
        {
            Assert.NotEqual(preset.Start, preset.End);
            Assert.NotEmpty(preset.Days);
        });
    }

    [Fact]
    public void Applying_a_preset_sets_the_hours_and_days_and_nothing_else()
    {
        var before = new AppSettings { Name = "Kai", IntervalMinutes = 30 };

        var office = WorkHoursPresets.All.Single(p => p.Id == "office").Apply(before);

        Assert.Equal(new TimeOnly(9, 0), office.ActiveStart);
        Assert.Equal(new TimeOnly(18, 0), office.ActiveEnd);
        Assert.DoesNotContain(DayOfWeek.Saturday, office.ActiveDays);
        Assert.DoesNotContain(DayOfWeek.Sunday, office.ActiveDays);
        Assert.Equal(5, office.ActiveDays.Length);
        Assert.Equal("Kai", office.Name);
        Assert.Equal(30, office.IntervalMinutes);
    }

    [Fact]
    public void The_matching_preset_is_found_and_goes_away_when_the_schedule_is_customised()
    {
        var settings = WorkHoursPresets.All.Single(p => p.Id == "nightowl").Apply(new AppSettings());
        Assert.Equal("nightowl", WorkHoursPresets.Matching(settings)?.Id);

        Assert.Null(WorkHoursPresets.Matching(settings with { ActiveEnd = new TimeOnly(3, 0) }));
        Assert.Null(WorkHoursPresets.Matching(settings with { ActiveDays = [DayOfWeek.Monday] }));
    }

    [Fact]
    public void The_default_schedule_matches_the_everyday_preset()
    {
        Assert.Equal("everyday", WorkHoursPresets.Matching(new AppSettings())?.Id);
    }

    [Fact]
    public void Night_owl_hours_cross_midnight_and_office_hours_skip_the_weekend()
    {
        var nightOwl = WorkHoursPresets.All.Single(p => p.Id == "nightowl").Apply(new AppSettings());
        Assert.True(ActiveHours.Contains(nightOwl, new DateTime(2026, 3, 11, 1, 0, 0)));
        Assert.False(ActiveHours.Contains(nightOwl, new DateTime(2026, 3, 11, 9, 0, 0)));

        var office = WorkHoursPresets.All.Single(p => p.Id == "office").Apply(new AppSettings());
        Assert.True(ActiveHours.Contains(office, new DateTime(2026, 3, 10, 10, 0, 0)));    // Tuesday
        Assert.False(ActiveHours.Contains(office, new DateTime(2026, 3, 14, 10, 0, 0)));   // Saturday
        Assert.False(ActiveHours.Contains(office, new DateTime(2026, 3, 10, 18, 0, 0)));   // 18:00 is the end
    }

    [Fact]
    public void Applying_a_preset_through_the_service_saves_it()
    {
        var settings = new SettingsService(_data.Location);
        settings.Load();

        settings.Update(WorkHoursPresets.All.Single(p => p.Id == "early").Apply);

        var reloaded = new SettingsService(_data.Location);
        reloaded.Load();
        Assert.Equal("early", WorkHoursPresets.Matching(reloaded.Current)?.Id);
    }
}
