using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using HeySip.App.Shared.Controls;
using HeySip.Core.Features.Companion;

namespace HeySip.Tests.Ui;

/// <summary>
/// Draws every character in every pose on one sheet, to look at. Set HEYSIP_SHEET to a file path to write it, for example
/// HEYSIP_SHEET=characters.png. Without it this test does nothing.
/// </summary>
public sealed class CharacterSheetTests
{
    private const string OutputVariable = "HEYSIP_SHEET";

    [AvaloniaFact]
    public void Draw_the_character_sheet_when_asked_to()
    {
        var path = Environment.GetEnvironmentVariable(OutputVariable);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var poses = Enum.GetValues<DripPose>();
        var characters = CharacterCatalog.All.Select(entry => entry.Id).ToArray();

        // HEYSIP_SHEET_PACK: a .heysippack to draw as an extra row, without installing it.
        HeySip.Core.Features.Companion.Packs.CharacterPack? extra = null;
        if (Environment.GetEnvironmentVariable("HEYSIP_SHEET_PACK") is { Length: > 0 } packFile)
        {
            using var stream = File.OpenRead(packFile);
            extra = HeySip.Core.Features.Companion.Packs.PackLoader.FromZip(stream, new HeySip.Core.Features.Companion.Packs.PackLoadOptions(new Version(1, 0))).Pack as HeySip.Core.Features.Companion.Packs.CharacterPack
                ?? throw new InvalidOperationException("HEYSIP_SHEET_PACK is not a valid character pack");
            characters = [.. characters, extra.Header.Id];
        }

        const int cellWidth = 110;
        const int cellHeight = 150;

        var grid = new Grid { Background = Brushes.White };
        for (var column = 0; column < poses.Length; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(cellWidth, GridUnitType.Pixel));
        }

        for (var row = 0; row < characters.Length; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition(cellHeight, GridUnitType.Pixel));
        }

        for (var row = 0; row < characters.Length; row++)
        {
            for (var column = 0; column < poses.Length; column++)
            {
                var drip = new DripControl { Character = characters[row], Pose = poses[column], IsAnimated = false, Width = 100, Height = 140 };
                if (extra is not null && characters[row] == extra.Header.Id)
                {
                    drip.Pack = extra;
                }
                Grid.SetRow(drip, row);
                Grid.SetColumn(drip, column);
                grid.Children.Add(drip);
            }
        }

        var size = new Size(cellWidth * poses.Length, cellHeight * characters.Length);
        grid.Measure(size);
        grid.Arrange(new Rect(size));
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)size.Width, (int)size.Height));
        bitmap.Render(grid);
#pragma warning disable CS0618
        bitmap.Save(path);
#pragma warning restore CS0618
    }

    /// <summary>
    /// Draws every accessory on every character, to look at. Set HEYSIP_OUTFIT_SHEET to a file path, and optionally
    /// HEYSIP_OUTFIT_POSE to a pose name (such as Waving), and HEYSIP_SHEET_INSTALLED to a folder of installed packs
    /// (<c>id\version\pack.json</c>) to draw those too. Without the path this test does nothing.
    /// </summary>
    [AvaloniaFact]
    public void Draw_the_outfit_sheet_when_asked_to()
    {
        var path = Environment.GetEnvironmentVariable("HEYSIP_OUTFIT_SHEET");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (Environment.GetEnvironmentVariable("HEYSIP_SHEET_INSTALLED") is { Length: > 0 } installed)
        {
            CharacterCatalog.Configure(installed);
        }

        var pose = Enum.TryParse<DripPose>(Environment.GetEnvironmentVariable("HEYSIP_OUTFIT_POSE"), ignoreCase: true, out var chosen) ? chosen : DripPose.Idle;
        var characters = CharacterCatalog.All.Select(entry => entry.Id).ToArray();
        var outfits = new List<IReadOnlyList<HeySip.Core.Features.Companion.Packs.AccessoryPack>?> { null };
        outfits.AddRange(CharacterCatalog.Accessories.Select(entry => (IReadOnlyList<HeySip.Core.Features.Companion.Packs.AccessoryPack>?)[entry.Pack]));
        const int cellWidth = 110;
        const int cellHeight = 150;

        var grid = new Grid { Background = Brushes.White };
        for (var column = 0; column < outfits.Count; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(cellWidth, GridUnitType.Pixel));
        }

        for (var row = 0; row < characters.Length; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition(cellHeight, GridUnitType.Pixel));
        }

        for (var row = 0; row < characters.Length; row++)
        {
            for (var column = 0; column < outfits.Count; column++)
            {
                var drip = new DripControl { Character = characters[row], Pose = pose, Outfit = outfits[column], IsAnimated = false, Width = 100, Height = 140 };
                Grid.SetRow(drip, row);
                Grid.SetColumn(drip, column);
                grid.Children.Add(drip);
            }
        }

        var size = new Size(cellWidth * outfits.Count, cellHeight * characters.Length);
        grid.Measure(size);
        grid.Arrange(new Rect(size));
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)size.Width, (int)size.Height));
        bitmap.Render(grid);
#pragma warning disable CS0618
        bitmap.Save(path);
#pragma warning restore CS0618
        CharacterCatalog.Configure(null);
    }

    /// <summary>
    /// Draws the Settings page filtered to the outfit, to look at. Set HEYSIP_SETTINGS_SHOT to a file path; optionally
    /// HEYSIP_SETTINGS_SEARCH to another search, and HEYSIP_SETTINGS_WEARING to "slot=id,slot=id" to dress the character first.
    /// </summary>
    [AvaloniaFact]
    public void Draw_the_outfit_settings_when_asked_to()
    {
        var path = Environment.GetEnvironmentVariable("HEYSIP_SETTINGS_SHOT");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        using var ui = UiHarness.Create();
        var settings = ui.Get<HeySip.Core.Features.Settings.SettingsService>();
        settings.Update(s => s with { Onboarded = true, Character = Environment.GetEnvironmentVariable("HEYSIP_SETTINGS_CHARACTER") ?? "heysip.axolotl" });
        foreach (var pair in (Environment.GetEnvironmentVariable("HEYSIP_SETTINGS_WEARING") ?? "hat=heysip.crown,eyes=heysip.round-glasses").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=');
            var slot = Enum.Parse<HeySip.Core.Features.Companion.Packs.AccessorySlot>(parts[0], ignoreCase: true);
            settings.Update(s => s.WithAccessory(slot, parts[1]));
        }

        ui.Shell.OpenSettings(Environment.GetEnvironmentVariable("HEYSIP_SETTINGS_SEARCH") ?? "outfit");
        ui.Show();
        using var frame = ui.Window.CaptureRenderedFrame()!;
#pragma warning disable CS0618
        frame.Save(path);
#pragma warning restore CS0618
    }

    /// <summary>
    /// Draws the install card for a pack, to look at. Set HEYSIP_INSTALL_SHOT to a file path; HEYSIP_INSTALL_KIND to
    /// "accessory" for an accessory (default: a character); HEYSIP_INSTALL_STAGE to "done" for the card after installing.
    /// </summary>
    [AvaloniaFact]
    public void Draw_the_install_card_when_asked_to()
    {
        var path = Environment.GetEnvironmentVariable("HEYSIP_INSTALL_SHOT");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var accessory = string.Equals(Environment.GetEnvironmentVariable("HEYSIP_INSTALL_KIND"), "accessory", StringComparison.OrdinalIgnoreCase);
        using var ui = UiHarness.Create();
        ui.Get<HeySip.Core.Features.Settings.SettingsService>().Update(s => s with { Onboarded = true, Character = "heysip.duckling" });
        CharacterCatalog.Configure(ui.Data.Location.CharactersDirectory);

        // Take one of the built-in packs and give it an id of its own, as if it came from somebody else.
        using var source = Avalonia.Platform.AssetLoader.Open(new Uri(accessory
            ? "avares://HeySip/Assets/Accessories/heysip.crown.json"
            : "avares://HeySip/Assets/Characters/heysip.axolotl.json"));
        var manifest = System.Text.Json.Nodes.JsonNode.Parse(source)!.AsObject();
        manifest["id"] = accessory ? "alice.royal-crown" : "alice.axolotl";
        manifest["name"] = accessory ? "Royal crown" : "Axolotl Deluxe";
        manifest["author"] = "Alice";
        manifest["version"] = "1.2.0";
        manifest["distribution"] = new System.Text.Json.Nodes.JsonObject { ["tier"] = "premium" };

        var file = Path.Combine(Path.GetTempPath(), "heysip-shot-" + Guid.NewGuid().ToString("N") + ".heysippack");
        using (var stream = File.Create(file))
        using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create))
        using (var entry = zip.CreateEntry("pack.json").Open())
        {
            entry.Write(System.Text.Encoding.UTF8.GetBytes(manifest.ToJsonString()));
        }

        ui.Shell.OpenPackFile(file);
        if (string.Equals(Environment.GetEnvironmentVariable("HEYSIP_INSTALL_STAGE"), "done", StringComparison.OrdinalIgnoreCase))
        {
            ui.Shell.InstallPreview.InstallCommand.Execute(null);
        }

        ui.Show();
        using var frame = ui.Window.CaptureRenderedFrame()!;
#pragma warning disable CS0618
        frame.Save(path);
#pragma warning restore CS0618
        File.Delete(file);
        CharacterCatalog.Configure(null);
    }
}
