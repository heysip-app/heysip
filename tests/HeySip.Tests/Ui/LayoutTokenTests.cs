using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;

namespace HeySip.Tests.Ui;

/// <summary>Views and styles name their sizes instead of writing numbers, so every name they use must exist.</summary>
public sealed partial class LayoutTokenTests
{
    [GeneratedRegex(@"\{(?:Dynamic|Static)Resource (Hb\w+)\}")]
    private static partial Regex TokenReference();

    private static string AppFolder()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !Directory.Exists(Path.Combine(folder.FullName, "src", "HeySip.App")))
        {
            folder = folder.Parent;
        }

        return Path.Combine(folder?.FullName ?? throw new DirectoryNotFoundException("The source folder was not found."), "src", "HeySip.App");
    }

    [AvaloniaFact]
    public void Every_token_used_in_a_view_or_style_is_defined()
    {
        using var ui = UiHarness.Create();
        var files = Directory.EnumerateFiles(AppFolder(), "*.axaml", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToList();
        Assert.NotEmpty(files);

        var missing = files
            .SelectMany(file => TokenReference().Matches(File.ReadAllText(file)).Select(match => (File: Path.GetFileName(file), Token: match.Groups[1].Value)))
            .Where(use => !Application.Current!.TryGetResource(use.Token, ThemeVariant.Light, out _) && !Application.Current.TryGetResource(use.Token, ThemeVariant.Dark, out _))
            .Select(use => $"{use.File}: {use.Token}")
            .Distinct()
            .ToList();

        Assert.True(missing.Count == 0, "Undefined tokens: " + string.Join(", ", missing));
    }
}
