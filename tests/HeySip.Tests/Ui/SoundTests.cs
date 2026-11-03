using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using HeySip.App.Shared.Audio;
using HeySip.App.Shared.Controls;
using HeySip.Core.Features.Companion;
using HeySip.Core.Features.Companion.Packs;
using HeySip.Core.Features.Hydration;
using HeySip.Core.Features.Scheduling;
using HeySip.Core.Features.Settings;
using HeySip.Core.SharedKernel.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace HeySip.Tests.Ui;

public sealed class SoundTests : IDisposable
{
    /// <summary>A speaker that plays nothing and remembers what it was asked to.</summary>
    private sealed class FakeSpeaker : ISoundOutput
    {
        public List<(byte[] Wav, float Gain)> Played { get; } = [];

        public bool Play(byte[] wav, float gain)
        {
            Played.Add((wav, gain));
            return true;
        }
    }

    private readonly FakeSpeaker _speaker = new();

    public void Dispose() => CharacterCatalog.Configure(null);

    private UiHarness Harness()
    {
        var ui = UiHarness.Create(services => services.AddSingleton<ISoundOutput>(_speaker));
        ui.Get<SettingsService>().Update(s => s with { Onboarded = true });
        ui.Get<ReminderService>().Restart();
        CharacterCatalog.Configure(ui.Data.Location.CharactersDirectory);
        ui.Get<SoundService>().Attach();
        return ui;
    }

    private static byte[] BuiltIn(string name)
    {
        using var stream = AssetLoader.Open(new Uri($"avares://HeySip/Assets/Sounds/{name}"));
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static byte[] Wav(int milliseconds, byte fill = 0)
    {
        const int rate = 8000;
        var dataSize = rate * milliseconds / 1000;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataSize);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(rate);
        writer.Write(rate);
        writer.Write((short)1);
        writer.Write((short)8);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);
        writer.Write(Enumerable.Repeat(fill, dataSize).ToArray());
        return stream.ToArray();
    }

    /// <summary>Installs a character pack whose own sounds are the given files; the character is not chosen yet.</summary>
    private static void InstallCharacterWithSounds(UiHarness ui, string id, Dictionary<string, byte[]> sounds, string? reminder = null, string? drank = null)
    {
        JsonArray Point(double x, double y) => new(x, y);
        var manifest = new JsonObject
        {
            ["formatVersion"] = 1,
            ["kind"] = "character",
            ["id"] = id,
            ["name"] = "Singer",
            ["version"] = "1.0.0",
            ["palette"] = new JsonObject { ["light"] = "#FFFFFF", ["deep"] = "#888888", ["outline"] = "#444444" },
            ["anchors"] = new JsonObject
            {
                ["eyeLeft"] = Point(39, 86), ["eyeRight"] = Point(61, 86), ["mouth"] = Point(50, 98),
                ["cheekLeft"] = Point(29, 98), ["cheekRight"] = Point(71, 98),
                ["shoulderLeft"] = Point(16, 92), ["shoulderRight"] = Point(84, 92),
                ["headTop"] = Point(50, 6), ["eyeLine"] = Point(50, 86), ["neck"] = Point(50, 64), ["handHold"] = Point(72, 108),
            },
            ["body"] = new JsonArray(new JsonObject { ["ellipse"] = new JsonArray(50, 80, 36, 40), ["fill"] = "$light" }),
        };
        var names = new JsonObject();
        if (reminder is not null)
        {
            names["reminder"] = reminder;
        }

        if (drank is not null)
        {
            names["drank"] = drank;
        }

        manifest["sounds"] = names;

        var files = new Dictionary<string, byte[]> { ["pack.json"] = Encoding.UTF8.GetBytes(manifest.ToJsonString()) };
        foreach (var (name, data) in sounds)
        {
            files[name] = data;
        }

        var loaded = PackLoader.FromFiles(files, new PackLoadOptions(new Version(1, 0)));
        Assert.True(loaded.Ok, loaded.Error);
        Assert.True(PackInstaller.Install(loaded, ui.Data.Location.CharactersDirectory).Ok);
        CharacterCatalog.Configure(ui.Data.Location.CharactersDirectory);
    }

    // ---- the built-in sounds

    [AvaloniaFact]
    public void The_built_in_chime_and_gulp_pass_the_same_checks_as_a_packs_sounds()
    {
        foreach (var name in new[] { "chime.wav", "gulp.wav" })
        {
            var data = BuiltIn(name);

            Assert.Null(SoundCheck.Validate(name, data));
            Assert.True(data.Length > 1000, name);
        }
    }

    // ---- when they play

    [AvaloniaFact]
    public void A_reminder_plays_the_chime_at_the_chosen_volume()
    {
        using var ui = Harness();

        ui.Get<ReminderService>().Trigger();

        var played = Assert.Single(_speaker.Played);
        Assert.Equal(BuiltIn("chime.wav"), played.Wav);
        Assert.Equal(0.16f, played.Gain, precision: 4); // the default volume, 40
    }

    [AvaloniaFact]
    public void Logging_a_drink_plays_the_gulp()
    {
        using var ui = Harness();

        ui.Get<HydrationService>().LogGlass();

        var played = Assert.Single(_speaker.Played);
        Assert.Equal(BuiltIn("gulp.wav"), played.Wav);
    }

    [AvaloniaFact]
    public void Filling_in_a_forgotten_drink_for_earlier_makes_no_sound()
    {
        using var ui = Harness();
        var yesterday = DateOnly.FromDateTime(DateTime.Now.AddDays(-1));

        ui.Get<HydrationService>().LogAt(yesterday, new TimeOnly(9, 0), 250);

        Assert.Empty(_speaker.Played);
    }

    [AvaloniaFact]
    public void A_louder_volume_plays_louder()
    {
        using var ui = Harness();
        ui.Get<SettingsService>().Update(s => s with { SoundVolume = 100 });

        ui.Get<HydrationService>().LogGlass();

        Assert.Equal(1f, Assert.Single(_speaker.Played).Gain);
    }

    [AvaloniaFact]
    public void Nothing_plays_when_sounds_are_off_or_the_volume_is_zero()
    {
        using var ui = Harness();
        var settings = ui.Get<SettingsService>();

        settings.Update(s => s with { SoundEnabled = false });
        ui.Get<ReminderService>().Trigger();
        ui.Get<HydrationService>().LogGlass();

        settings.Update(s => s with { SoundEnabled = true, SoundVolume = 0 });
        ui.Get<ReminderService>().Trigger();
        ui.Get<HydrationService>().LogGlass();

        Assert.Empty(_speaker.Played);
    }

    [AvaloniaFact]
    public void Nothing_plays_while_reminders_are_paused_and_sounds_return_when_they_resume()
    {
        using var ui = Harness();
        var reminders = ui.Get<ReminderService>();

        reminders.Pause(TimeSpan.FromHours(1));
        reminders.Trigger();
        ui.Get<HydrationService>().LogGlass();
        Assert.Empty(_speaker.Played);

        reminders.Resume();
        ui.Get<HydrationService>().LogGlass();
        Assert.Single(_speaker.Played);
    }

    [AvaloniaFact]
    public void Quiet_time_keeps_every_sound_silent_too()
    {
        using var ui = Harness();

        ui.Get<ReminderService>().PauseUntilTomorrow();
        ui.Get<HydrationService>().LogGlass();

        Assert.Empty(_speaker.Played);
    }

    [AvaloniaFact]
    public void A_reminder_that_arrives_while_another_is_showing_gets_no_second_chime()
    {
        using var ui = Harness();
        var reminders = ui.Get<ReminderService>();
        reminders.ReminderVisible = true;

        reminders.Trigger();

        Assert.Empty(_speaker.Played);
    }

    // ---- the test button and the settings

    [AvaloniaFact]
    public void The_test_button_plays_the_chime_even_during_a_pause_but_not_when_sounds_are_off()
    {
        using var ui = Harness();
        ui.Get<ReminderService>().Pause(TimeSpan.FromHours(1));

        ui.Shell.Settings.TestSoundCommand.Execute(null);

        Assert.Equal(BuiltIn("chime.wav"), Assert.Single(_speaker.Played).Wav);

        ui.Get<SettingsService>().Update(s => s with { SoundEnabled = false });
        ui.Shell.Settings.TestSoundCommand.Execute(null);

        Assert.Single(_speaker.Played);
    }

    [AvaloniaFact]
    public void Changing_the_switch_and_the_slider_in_settings_saves_them()
    {
        using var ui = Harness();
        var page = ui.Shell.Settings;
        Assert.True(page.SoundsOn);
        Assert.Equal(40, page.SoundVolume);

        page.SoundVolume = 72.4;
        page.SoundsOn = false;

        Assert.Equal(72, ui.Get<SettingsService>().Current.SoundVolume);
        Assert.False(ui.Get<SettingsService>().Current.SoundEnabled);
    }

    [AvaloniaFact]
    public void Settings_follows_a_change_made_elsewhere_and_can_be_found_by_searching()
    {
        using var ui = Harness();

        ui.Get<SettingsService>().Update(s => s with { SoundEnabled = false, SoundVolume = 15 });

        Assert.False(ui.Shell.Settings.SoundsOn);
        Assert.Equal(15, ui.Shell.Settings.SoundVolume);

        foreach (var word in new[] { "sounds", "volume", "mute", "chime", "gulp" })
        {
            ui.Shell.OpenSettings(word);
            Assert.True(ui.Shell.Settings.ShowSounds, word);
            Assert.True(ui.Shell.Settings.ShowCompanionCard, word);
        }

        ui.Shell.OpenSettings("interval");
        Assert.False(ui.Shell.Settings.ShowSounds);
    }

    [AvaloniaFact]
    public void The_palette_mutes_and_turns_sounds_back_on_offering_only_the_one_that_applies()
    {
        using var ui = Harness();
        var palette = ui.Shell.Palette;

        palette.Open();
        Assert.Contains(palette.Results, item => item.Command.Id == "sound.mute");
        Assert.DoesNotContain(palette.Results, item => item.Command.Id == "sound.unmute");
        palette.Results.First(item => item.Command.Id == "sound.mute").RunCommand.Execute(null);

        Assert.False(ui.Get<SettingsService>().Current.SoundEnabled);

        palette.Open();
        Assert.Contains(palette.Results, item => item.Command.Id == "sound.unmute");
        Assert.DoesNotContain(palette.Results, item => item.Command.Id == "sound.mute");
        palette.Results.First(item => item.Command.Id == "sound.unmute").RunCommand.Execute(null);

        Assert.True(ui.Get<SettingsService>().Current.SoundEnabled);
    }

    // ---- a character's own sounds

    [AvaloniaFact]
    public void A_character_with_its_own_sounds_plays_those_instead()
    {
        using var ui = Harness();
        var theirChime = Wav(300, fill: 100);
        var theirGulp = Wav(200, fill: 50);
        InstallCharacterWithSounds(ui, "tester.singer",
            new() { ["sounds/ding.wav"] = theirChime, ["sounds/glug.wav"] = theirGulp },
            reminder: "sounds/ding.wav", drank: "sounds/glug.wav");
        ui.Get<SettingsService>().Update(s => s with { Character = "tester.singer" });

        ui.Get<ReminderService>().Trigger();
        ui.Get<HydrationService>().LogGlass();

        Assert.Equal([theirChime, theirGulp], _speaker.Played.Select(item => item.Wav));
    }

    [AvaloniaFact]
    public void A_character_with_only_one_sound_uses_the_built_in_for_the_other()
    {
        using var ui = Harness();
        var theirChime = Wav(300, fill: 100);
        InstallCharacterWithSounds(ui, "tester.singer", new() { ["sounds/ding.wav"] = theirChime }, reminder: "sounds/ding.wav");
        ui.Get<SettingsService>().Update(s => s with { Character = "tester.singer" });

        ui.Get<ReminderService>().Trigger();
        ui.Get<HydrationService>().LogGlass();

        Assert.Equal([theirChime, BuiltIn("gulp.wav")], _speaker.Played.Select(item => item.Wav));
    }

    [AvaloniaFact]
    public void A_character_cannot_override_mute_volume_or_pause()
    {
        using var ui = Harness();
        InstallCharacterWithSounds(ui, "tester.singer", new() { ["sounds/ding.wav"] = Wav(300) }, reminder: "sounds/ding.wav");
        var settings = ui.Get<SettingsService>();
        settings.Update(s => s with { Character = "tester.singer", SoundEnabled = false });

        ui.Get<ReminderService>().Trigger();
        Assert.Empty(_speaker.Played);

        settings.Update(s => s with { SoundEnabled = true, SoundVolume = 100 });
        ui.Get<ReminderService>().Trigger();
        Assert.Equal(1f, Assert.Single(_speaker.Played).Gain); // the volume is the user's, not the pack's

        ui.Get<ReminderService>().Pause(TimeSpan.FromHours(1));
        ui.Get<ReminderService>().Trigger();
        Assert.Single(_speaker.Played);
    }

    [AvaloniaFact]
    public void A_sound_file_that_was_changed_after_installing_is_not_trusted()
    {
        using var ui = Harness();
        InstallCharacterWithSounds(ui, "tester.singer", new() { ["sounds/ding.wav"] = Wav(300) }, reminder: "sounds/ding.wav");
        ui.Get<SettingsService>().Update(s => s with { Character = "tester.singer" });
        File.WriteAllText(Path.Combine(ui.Data.Location.CharactersDirectory, "tester.singer", "1.0.0", "sounds", "ding.wav"), "not audio any more");
        CharacterCatalog.Configure(ui.Data.Location.CharactersDirectory);

        ui.Get<ReminderService>().Trigger();

        Assert.Equal(BuiltIn("chime.wav"), Assert.Single(_speaker.Played).Wav);
    }

    [AvaloniaFact]
    public void A_deleted_sound_file_falls_back_to_the_built_in_sound()
    {
        using var ui = Harness();
        InstallCharacterWithSounds(ui, "tester.singer", new() { ["sounds/ding.wav"] = Wav(300) }, reminder: "sounds/ding.wav");
        ui.Get<SettingsService>().Update(s => s with { Character = "tester.singer" });
        File.Delete(Path.Combine(ui.Data.Location.CharactersDirectory, "tester.singer", "1.0.0", "sounds", "ding.wav"));
        CharacterCatalog.Configure(ui.Data.Location.CharactersDirectory);

        ui.Get<ReminderService>().Trigger();

        Assert.Equal(BuiltIn("chime.wav"), Assert.Single(_speaker.Played).Wav);
    }

    [AvaloniaFact]
    public void An_ogg_sound_in_a_pack_is_played_as_decoded_audio()
    {
        using var ui = Harness();
        var ogg = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "tone.ogg"));
        InstallCharacterWithSounds(ui, "tester.singer", new() { ["sounds/ding.ogg"] = ogg }, reminder: "sounds/ding.ogg");
        ui.Get<SettingsService>().Update(s => s with { Character = "tester.singer" });

        ui.Get<ReminderService>().Trigger();

        var wav = Assert.Single(_speaker.Played).Wav;
        Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(wav, 8, 4));
    }

    [AvaloniaFact]
    public void Switching_back_to_a_built_in_character_returns_to_the_built_in_sounds()
    {
        using var ui = Harness();
        InstallCharacterWithSounds(ui, "tester.singer", new() { ["sounds/ding.wav"] = Wav(300) }, reminder: "sounds/ding.wav");
        var settings = ui.Get<SettingsService>();
        settings.Update(s => s with { Character = "tester.singer" });
        settings.Update(s => s with { Character = "heysip.axolotl" });

        ui.Get<ReminderService>().Trigger();

        Assert.Equal(BuiltIn("chime.wav"), Assert.Single(_speaker.Played).Wav);
    }

    // ---- turning an OGG file into something the player takes

    [Fact]
    public void An_ogg_file_becomes_a_wav_with_the_same_length_and_sample_rate()
    {
        var ogg = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "tone.ogg"));

        var wav = SoundDecoder.ToWav("tone.ogg", ogg)!;

        Assert.NotNull(wav);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(22)));          // mono
        Assert.Equal(22050, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(24)));      // sample rate
        Assert.Equal(16, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(34)));         // bits per sample
        var seconds = BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(40)) / 2.0 / 22050;
        Assert.InRange(seconds, 0.35, 0.45);                                              // the tone is 0.4 seconds long
        Assert.Null(SoundCheck.Validate("tone.wav", wav));
    }

    [Fact]
    public void The_decoded_sound_is_really_audio_and_not_silence()
    {
        var ogg = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "tone.ogg"));

        var wav = SoundDecoder.ToWav("tone.ogg", ogg)!;

        var peak = Enumerable.Range(0, (wav.Length - 44) / 2).Max(i => Math.Abs((int)BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(44 + (i * 2)))));
        Assert.InRange(peak, 8000, 20000); // the tone was written at half volume (about 16000 of 32767)
    }

    [Fact]
    public void A_wav_passes_through_untouched_and_other_formats_are_not_played()
    {
        var wav = Wav(100);

        Assert.Same(wav, SoundDecoder.ToWav("a.wav", wav));
        Assert.Null(SoundDecoder.ToWav("a.mp3", wav));
        Assert.Null(SoundDecoder.ToWav("a", wav));
    }

    [Fact]
    public void Garbage_and_damaged_ogg_files_give_no_sound_instead_of_an_error()
    {
        var ogg = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "tone.ogg"));

        Assert.Null(SoundDecoder.OggToWav(Encoding.ASCII.GetBytes("this is not an ogg file")));
        Assert.Null(SoundDecoder.OggToWav([]));
        Assert.Null(SoundDecoder.OggToWav(ogg[..30]));
        Assert.Null(SoundDecoder.OggToWav(new byte[2000]));
    }

    // ---- the real speaker

    [Fact]
    public void A_speaker_that_has_been_let_go_plays_nothing_and_touches_no_hardware()
    {
        var output = new SoundFlowOutput(new DiagnosticLog(TimeProvider.System, Path.Combine(Path.GetTempPath(), "heysip-sound-log-" + Guid.NewGuid().ToString("N"))));
        output.Dispose();

        Assert.False(output.Play(Wav(100), 0.5f));
        output.Dispose(); // letting go twice is fine
    }

    /// <summary>
    /// Plays the built-in gulp very quietly through this computer's real speakers, to prove the whole path works: the audio
    /// engine starts, the sound plays to its end, and letting go of the device afterwards does not hang. Set
    /// HEYSIP_AUDIO_SMOKE=1 to run it; without that it does nothing, so test runs stay silent.
    /// </summary>
    [AvaloniaFact]
    public async Task The_real_speakers_play_the_gulp_and_can_be_let_go_afterwards()
    {
        if (Environment.GetEnvironmentVariable("HEYSIP_AUDIO_SMOKE") != "1")
        {
            return;
        }

        var log = new DiagnosticLog(TimeProvider.System, Path.Combine(Path.GetTempPath(), "heysip-audio-smoke-" + Guid.NewGuid().ToString("N")));
        var output = new SoundFlowOutput(log);

        Assert.True(output.Play(BuiltIn("gulp.wav"), 0.02f), "the sound could not be started: " + string.Join(" | ", log.Tail()));
        Assert.True(output.Play(BuiltIn("gulp.wav"), 0.02f), "a second sound at the same time could not be started");
        Assert.True(output.IsDeviceOpen);
        await Task.Delay(TimeSpan.FromSeconds(1.5)); // the gulp is half a second long
        Assert.True(output.IsDeviceOpen, "the device is kept for a few seconds in case another sound follows");

        // Then it is let go, so a tray app sitting idle all day doesn't hold the sound hardware.
        await Task.Delay(TimeSpan.FromSeconds(10));
        Assert.False(output.IsDeviceOpen, "the audio device was not released after the sounds ended");

        Assert.True(output.Play(BuiltIn("gulp.wav"), 0.02f), "a sound after the device was released could not be started");
        Assert.True(output.IsDeviceOpen);

        var released = Task.Run(output.Dispose);
        Assert.True(await Task.WhenAny(released, Task.Delay(TimeSpan.FromSeconds(10))) == released, "letting go of the audio device hung");
        Assert.False(output.Play(BuiltIn("gulp.wav"), 0.02f));
        Assert.DoesNotContain(log.Tail(), line => line.Contains("WARN") || line.Contains("ERROR"));
    }
}
