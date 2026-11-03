using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using HeySip.App.Shared.Audio;
using HeySip.App.Shared.Controls;
using HeySip.Core.Features.Companion.Packs;
using HeySip.Core.Features.Scheduling;
using HeySip.Core.Features.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace HeySip.Tests.Ui;

/// <summary>Tests for the problems a code review of the pack, install and sound work turned up.</summary>
public sealed class ReviewFixTests : IDisposable
{
    private sealed class Speaker(Func<byte[], bool> accepts) : ISoundOutput
    {
        public List<byte[]> Asked { get; } = [];

        public List<byte[]> Played { get; } = [];

        public bool Play(byte[] wav, float gain)
        {
            Asked.Add(wav);
            if (!accepts(wav))
            {
                return false;
            }

            Played.Add(wav);
            return true;
        }
    }

    private readonly string _files = Path.Combine(Path.GetTempPath(), "heysip-reviewfix-" + Guid.NewGuid().ToString("N"));

    public ReviewFixTests() => Directory.CreateDirectory(_files);

    public void Dispose()
    {
        CharacterCatalog.Configure(null);
        if (Directory.Exists(_files))
        {
            Directory.Delete(_files, recursive: true);
        }
    }

    private static UiHarness Harness(ISoundOutput? speaker = null)
    {
        var ui = UiHarness.Create(services =>
        {
            if (speaker is not null)
            {
                services.AddSingleton(speaker);
            }
        });
        ui.Get<SettingsService>().Update(s => s with { Onboarded = true });
        ui.Get<ReminderService>().Restart();
        CharacterCatalog.Configure(ui.Data.Location.CharactersDirectory);
        return ui;
    }

    private static JsonArray Point(double x, double y) => new(x, y);

    private static JsonObject Manifest(string id = "tester.blob", string? minApp = null, string? reminder = null)
    {
        var manifest = new JsonObject
        {
            ["formatVersion"] = 1,
            ["kind"] = "character",
            ["id"] = id,
            ["name"] = "Blob",
            ["version"] = "1.0.0",
            ["palette"] = new JsonObject { ["light"] = "#C4B5FD", ["deep"] = "#7C3AED", ["outline"] = "#4C1D95" },
            ["anchors"] = new JsonObject
            {
                ["eyeLeft"] = Point(39, 86), ["eyeRight"] = Point(61, 86), ["mouth"] = Point(50, 98),
                ["cheekLeft"] = Point(29, 98), ["cheekRight"] = Point(71, 98),
                ["shoulderLeft"] = Point(16, 92), ["shoulderRight"] = Point(84, 92),
                ["headTop"] = Point(50, 6), ["eyeLine"] = Point(50, 86), ["neck"] = Point(50, 64), ["handHold"] = Point(72, 108),
            },
            ["body"] = new JsonArray(new JsonObject { ["ellipse"] = new JsonArray(50, 90, 42, 36), ["fill"] = "$light" }),
        };
        if (minApp is not null)
        {
            manifest["minAppVersion"] = minApp;
        }

        if (reminder is not null)
        {
            manifest["sounds"] = new JsonObject { ["reminder"] = reminder };
        }

        return manifest;
    }

    private static byte[] Wav(byte fill)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + 800);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(8000);
        writer.Write(8000);
        writer.Write((short)1);
        writer.Write((short)8);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(800);
        writer.Write(Enumerable.Repeat(fill, 800).ToArray());
        return stream.ToArray();
    }

    private static byte[] BuiltIn(string name)
    {
        using var stream = AssetLoader.Open(new Uri($"avares://HeySip/Assets/Sounds/{name}"));
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private string WritePackFile(JsonObject manifest)
    {
        var path = Path.Combine(_files, manifest["id"] + ".heysippack");
        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        using var content = zip.CreateEntry("pack.json").Open();
        content.Write(Encoding.UTF8.GetBytes(manifest.ToJsonString()));
        return path;
    }

    // ---- one idea of "this version of the app"

    [AvaloniaFact]
    public void The_install_card_and_the_installed_list_agree_about_what_the_app_can_run()
    {
        using var ui = Harness();
        var tooNew = Manifest(minApp: "0.9.0"); // newer than this build, whatever that build is

        ui.Shell.OpenPackFile(WritePackFile(tooNew));
        Assert.True(ui.Shell.InstallPreview.IsFailed);
        Assert.Contains("needs HeySip 0.9.0", ui.Shell.InstallPreview.ErrorText);

        // The same pack, put straight into the folder, is refused by the scan too.
        var folder = Path.Combine(ui.Data.Location.CharactersDirectory, "tester.blob", "1.0.0");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "pack.json"), tooNew.ToJsonString());
        CharacterCatalog.Configure(ui.Data.Location.CharactersDirectory);
        Assert.False(CharacterCatalog.Contains("tester.blob"));
    }

    [AvaloniaFact]
    public void A_pack_that_the_card_accepts_is_one_the_list_accepts()
    {
        using var ui = Harness();
        var fine = Manifest(minApp: "0.1.0");

        ui.Shell.OpenPackFile(WritePackFile(fine));
        Assert.True(ui.Shell.InstallPreview.IsPreview);
        ui.Shell.InstallPreview.InstallCommand.Execute(null);

        Assert.True(CharacterCatalog.Contains("tester.blob"));
    }

    // ---- settings must not be rewritten by accident

    [AvaloniaFact]
    public void Installing_or_removing_a_pack_never_rewrites_a_setting()
    {
        using var ui = Harness();
        var settings = ui.Get<SettingsService>();
        settings.Update(s => s with { TimeZoneId = "Mars/Olympus_Mons", Name = "Kai" });
        var before = settings.Current;

        CharacterCatalog.Configure(ui.Data.Location.CharactersDirectory); // what installing or removing triggers

        Assert.True(before.SameAs(settings.Current));
        Assert.Equal("Mars/Olympus_Mons", settings.Current.TimeZoneId);
    }

    [AvaloniaFact]
    public void A_time_zone_this_computer_does_not_list_survives_changing_something_else()
    {
        using var ui = Harness();
        var settings = ui.Get<SettingsService>();
        settings.Update(s => s with { TimeZoneId = "Mars/Olympus_Mons" });

        ui.Shell.Settings.SoundVolume = 12;
        ui.Shell.Settings.SoundsOn = false;
        ui.Shell.Settings.IntervalMinutes = 30;

        Assert.Equal("Mars/Olympus_Mons", settings.Current.TimeZoneId);
        Assert.Equal(12, settings.Current.SoundVolume);
    }

    [AvaloniaFact]
    public void A_listed_time_zone_can_still_be_chosen_and_automatic_can_still_be_chosen_again()
    {
        using var ui = Harness();
        var settings = ui.Get<SettingsService>();
        var listed = ui.Shell.Settings.Zones.First(zone => zone.Id is not null);

        ui.Shell.Settings.Zone = listed;
        Assert.Equal(listed.Id, settings.Current.TimeZoneId);

        ui.Shell.Settings.Zone = ui.Shell.Settings.Zones[0];
        Assert.Null(settings.Current.TimeZoneId);
    }

    // ---- removing

    [AvaloniaFact]
    public void A_removal_that_fails_says_so_and_a_later_one_clears_the_message()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // relies on Windows refusing to delete a folder with an open file in it
        }

        using var ui = Harness();
        ui.Shell.OpenPackFile(WritePackFile(Manifest()));
        ui.Shell.InstallPreview.InstallCommand.Execute(null);
        ui.Shell.InstallPreview.CloseCommand.Execute(null);
        var manifestFile = Path.Combine(ui.Data.Location.CharactersDirectory, "tester.blob", "1.0.0", "pack.json");

        using (new FileStream(manifestFile, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            ui.Shell.Settings.Installed.Single().RemoveCommand.Execute(null);

            Assert.Contains("couldn't be removed", ui.Shell.Settings.InstalledMessage);
            Assert.True(File.Exists(manifestFile));
        }

        ui.Shell.Settings.Installed.Single().RemoveCommand.Execute(null);

        Assert.Equal("", ui.Shell.Settings.InstalledMessage);
        Assert.False(ui.Shell.Settings.HasInstalled);
    }

    // ---- sounds

    [AvaloniaFact]
    public void A_pack_sound_the_player_cannot_use_falls_back_to_the_built_in_sound()
    {
        var theirs = Wav(100);
        var speaker = new Speaker(wav => !wav.SequenceEqual(theirs)); // the player refuses the pack's sound
        using var ui = Harness(speaker);
        ui.Get<SoundService>().Attach();
        Install(ui, Manifest(reminder: "sounds/ding.wav"), new() { ["sounds/ding.wav"] = theirs });
        ui.Get<SettingsService>().Update(s => s with { Character = "tester.blob" });

        ui.Get<ReminderService>().Trigger();

        Assert.Equal(2, speaker.Asked.Count);
        Assert.Equal(theirs, speaker.Asked[0]);
        Assert.Equal(BuiltIn("chime.wav"), Assert.Single(speaker.Played));
    }

    [AvaloniaFact]
    public void A_sound_file_that_could_not_be_read_once_is_tried_again_next_time()
    {
        var theirs = Wav(100);
        var speaker = new Speaker(_ => true);
        using var ui = Harness(speaker);
        ui.Get<SoundService>().Attach();
        Install(ui, Manifest(reminder: "sounds/ding.wav"), new() { ["sounds/ding.wav"] = theirs });
        ui.Get<SettingsService>().Update(s => s with { Character = "tester.blob" });
        var file = Path.Combine(ui.Data.Location.CharactersDirectory, "tester.blob", "1.0.0", "sounds", "ding.wav");
        File.Move(file, file + ".away");

        ui.Get<ReminderService>().Trigger();
        Assert.Equal(BuiltIn("chime.wav"), Assert.Single(speaker.Played)); // not there: the built-in sound

        File.Move(file + ".away", file);
        ui.Get<ReminderService>().Trigger();

        Assert.Equal(theirs, speaker.Played[^1]); // back again, and used, without restarting anything
    }

    private static void Install(UiHarness ui, JsonObject manifest, Dictionary<string, byte[]> extra)
    {
        var files = new Dictionary<string, byte[]> { ["pack.json"] = Encoding.UTF8.GetBytes(manifest.ToJsonString()) };
        foreach (var (name, data) in extra)
        {
            files[name] = data;
        }

        var loaded = PackLoader.FromFiles(files, new PackLoadOptions(new Version(1, 0)));
        Assert.True(loaded.Ok, loaded.Error);
        Assert.True(PackInstaller.Install(loaded, ui.Data.Location.CharactersDirectory).Ok);
        CharacterCatalog.Configure(ui.Data.Location.CharactersDirectory);
    }

    // ---- drawing does not need the catalogue on every frame

    [AvaloniaFact]
    public void A_character_is_looked_up_again_only_when_it_or_the_catalogue_changes()
    {
        using var ui = Harness();
        var before = CharacterCatalog.Version;

        CharacterCatalog.Configure(ui.Data.Location.CharactersDirectory);
        var afterRescan = CharacterCatalog.Version;
        _ = CharacterCatalog.Find("heysip.drip");
        _ = CharacterCatalog.Find("heysip.minty");

        Assert.True(afterRescan > before);
        Assert.Equal(afterRescan, CharacterCatalog.Version); // looking things up changes nothing
    }

    [AvaloniaFact]
    public void A_drawn_character_notices_when_its_pack_is_installed_while_it_is_on_screen()
    {
        using var ui = Harness();
        var drip = new HeySip.App.Shared.Controls.DripControl { Character = "tester.blob", IsAnimated = false, Width = 100, Height = 140 };
        byte[] Draw()
        {
            drip.Measure(new Avalonia.Size(100, 140));
            drip.Arrange(new Avalonia.Rect(0, 0, 100, 140));
            using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new Avalonia.PixelSize(100, 140));
            bitmap.Render(drip);
            using var stream = new MemoryStream();
#pragma warning disable CS0618
            bitmap.Save(stream);
#pragma warning restore CS0618
            return stream.ToArray();
        }

        var missing = Draw(); // not installed yet: the default character

        Install(ui, Manifest(), []);
        drip.InvalidateVisual();

        Assert.NotEqual(missing, Draw());
    }
}
