using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using HeySip.App.Shared.Controls;
using HeySip.Core.Features.Companion;
using HeySip.Tests.Support;

namespace HeySip.Tests.Ui;

public sealed class IconTests
{
    private static readonly int[] Sizes = [16, 24, 32, 48, 64, 128, 256];

    /// <summary>Set to a file path to regenerate the app icon: HEYSIP_ICON_OUT=src/HeySip.App/Assets/heysip.ico.</summary>
    private const string OutputVariable = "HEYSIP_ICON_OUT";

    private static byte[] RenderPng(Control content, int size)
    {
        content.Measure(new Size(size, size));
        content.Arrange(new Rect(0, 0, size, size));
        using var bitmap = new RenderTargetBitmap(new PixelSize(size, size));
        bitmap.Render(content);
        using var stream = new MemoryStream();
#pragma warning disable CS0618
        bitmap.Save(stream);
#pragma warning restore CS0618
        return stream.ToArray();
    }

    private static DripControl Drip(int size) => new()
    {
        Character = "heysip.drip",
        IsAnimated = false,
        Width = size,
        Height = size,
    };

    [AvaloniaFact]
    public void Drip_renders_something_at_every_icon_size()
    {
        foreach (var size in Sizes)
        {
            var blank = RenderPng(new Border { Width = size, Height = size }, size);
            var drip = RenderPng(Drip(size), size);

            Assert.NotEqual(blank, drip);
            Assert.True(drip.Length > blank.Length, $"The {size}px render looks empty.");
        }
    }

    [AvaloniaFact]
    public void The_app_icon_can_be_regenerated_from_the_mascot()
    {
        var images = Sizes.Select(size => (size, RenderPng(Drip(size), size))).ToList();
        var ico = IconWriter.Pack(images);

        // A valid ICO starts 00 00 01 00 and declares one entry per size.
        Assert.Equal([0, 0, 1, 0, (byte)Sizes.Length, 0], ico.Take(6));

        if (Environment.GetEnvironmentVariable(OutputVariable) is { Length: > 0 } path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllBytes(path, ico);
        }
    }
}
