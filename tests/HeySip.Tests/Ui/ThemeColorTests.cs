using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;

namespace HeySip.Tests.Ui;

/// <summary>Colours are written once, in the palette; the themes and views only refer to them by name.</summary>
public sealed partial class ThemeColorTests
{
    [GeneratedRegex(@"#[0-9A-Fa-f]{6,8}\b")]
    private static partial Regex HexColor();

    private static string AppFolder()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !Directory.Exists(Path.Combine(folder.FullName, "src", "HeySip.App")))
        {
            folder = folder.Parent;
        }

        return Path.Combine(folder?.FullName ?? throw new DirectoryNotFoundException("The source folder was not found."), "src", "HeySip.App");
    }

    [Fact]
    public void Only_the_palette_and_the_shadows_in_it_write_colours_out()
    {
        var offenders = Directory.EnumerateFiles(AppFolder(), "*.axaml", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && Path.GetFileName(file) != "Palette.axaml")
            .Where(file => HexColor().IsMatch(File.ReadAllText(file)))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Empty(offenders);
    }

    [AvaloniaTheory]
    [InlineData("Dark", "HbWindowBrush", 0x1E, 0x1F, 0x22)]
    [InlineData("Dark", "HbPanelBrush", 0x2B, 0x2D, 0x30)]
    [InlineData("Dark", "HbAccentBrush", 0x4D, 0xB6, 0xAC)]
    [InlineData("Light", "HbWindowBrush", 0xEB, 0xEC, 0xF0)]
    [InlineData("Light", "HbPanelBrush", 0xFF, 0xFF, 0xFF)]
    [InlineData("Light", "HbAccentBrush", 0x1F, 0x8A, 0x80)]
    public void A_theme_brush_resolves_to_the_colour_it_always_had(string theme, string key, int red, int green, int blue)
    {
        using var ui = UiHarness.Create();
        var variant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;

        Assert.True(Application.Current!.TryGetResource(key, variant, out var found));

        var color = Assert.IsAssignableFrom<ISolidColorBrush>(found).Color;
        Assert.Equal(Color.FromRgb((byte)red, (byte)green, (byte)blue), color);
    }

    [AvaloniaTheory]
    [InlineData("Dark", 0.55)]
    [InlineData("Light", 0.35)]
    public void The_overlay_keeps_its_strength_in_each_theme(string theme, double opacity)
    {
        using var ui = UiHarness.Create();
        var variant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;

        Assert.True(Application.Current!.TryGetResource("HbOverlayBrush", variant, out var found));

        Assert.Equal(opacity, Assert.IsAssignableFrom<IBrush>(found).Opacity, 3);
    }
}
