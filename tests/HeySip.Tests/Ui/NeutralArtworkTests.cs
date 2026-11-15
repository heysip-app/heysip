using Avalonia.Headless.XUnit;
using HeySip.App.Shared.Controls;
using HeySip.Core.Features.Companion.Packs;

namespace HeySip.Tests.Ui;

/// <summary>The artwork is built from named measurements; it must cover exactly the area the hand-written paths did.</summary>
public sealed class NeutralArtworkTests
{
    private static Avalonia.Rect Reach(string path, double stroke = 0) =>
        PreparedShape.Create(new PathShape(path) { Stroke = stroke > 0 ? new PackStroke(new PackColor(0, 0, 0), stroke) : null, Fill = stroke > 0 ? null : new SolidFill(new PackColor(0, 0, 0)) })!.Reach;

    [AvaloniaTheory]
    [InlineData(EyeStyle.Happy, "M-6,2 Q0,-7 6,2", 3)]
    [InlineData(EyeStyle.Rest, "M-6,1 Q0,6 6,1", 3)]
    public void Eye_arcs_match(EyeStyle style, string original, double stroke) =>
        Assert.Equal(Reach(original, stroke), Assert.Single(NeutralArtwork.Eyes[style]).Reach);

    [AvaloniaTheory]
    [InlineData(MouthStyle.Smile, "M-7,0 Q0,8 7,0", 2.8)]
    [InlineData(MouthStyle.Small, "M-4,3 Q0,5 4,3", 2.5)]
    [InlineData(MouthStyle.Worried, "M-7,7 Q0,0 7,7", 2.8)]
    [InlineData(MouthStyle.Sleepy, "M-5,4 Q0,1 5,4", 2.5)]
    public void Stroked_mouths_match(MouthStyle style, string original, double stroke) =>
        Assert.Equal(Reach(original, stroke), Assert.Single(NeutralArtwork.Mouths[style]).Reach);

    [AvaloniaFact]
    public void The_grin_matches() =>
        Assert.Equal(Reach("M-9,-1 Q0,16 9,-1 Z"), NeutralArtwork.Mouths[MouthStyle.Grin][0].Reach);

    [AvaloniaFact]
    public void The_glass_and_water_match()
    {
        Assert.Equal(Reach("M-10,-18 L10,-18 L7,6 L-7,6 Z", 1.6).Width, NeutralArtwork.Glass[0].Reach.Width);
        Assert.Equal(Reach("M-8.8,-9 L8.8,-9 L7,6 L-7,6 Z"), NeutralArtwork.Glass[1].Reach);
    }

    [AvaloniaFact]
    public void The_effects_match()
    {
        Assert.Equal(Reach("M0,0 L7,0 L0,9 L7,9", 1.8), NeutralArtwork.Letter.Reach);
        Assert.Equal(Reach("M0,-6 L1.5,-1.5 L6,0 L1.5,1.5 L0,6 L-1.5,1.5 L-6,0 L-1.5,-1.5 Z"), NeutralArtwork.Star.Reach);
        Assert.Equal(Reach("M0,-5 C0,-5 -4,0 -4,2.5 A4,4 0 0 0 4,2.5 C4,0 0,-5 0,-5 Z"), NeutralArtwork.Drop.Reach);
        Assert.Equal(Reach("M2.8,-9 Q7,-7 6,-3", 1.5).Width, NeutralArtwork.Note[2].Reach.Width);
    }
}
