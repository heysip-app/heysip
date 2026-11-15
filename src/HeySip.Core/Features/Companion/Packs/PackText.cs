namespace HeySip.Core.Features.Companion.Packs;

/// <summary>Small text helpers for the messages shown when a pack is refused.</summary>
internal static class PackText
{
    private const string Ellipsis = "…";

    /// <summary>Cuts <paramref name="text"/> to at most <paramref name="maxLength"/> characters, marking the cut, so a long value can't flood a message.</summary>
    public static string Shorten(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + Ellipsis;
}
