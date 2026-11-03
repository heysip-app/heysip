namespace HeySip.Core.SharedKernel;

/// <summary>
/// Ranks how well a typed query matches some text, the way a command palette does: every word the user types must
/// appear, in order, somewhere in the text, and tighter, earlier, word-aligned matches rank higher.
/// </summary>
public static class FuzzyMatcher
{
    private const int SubstringBase = 100;
    private const int SubstringPerChar = 2;
    private const int PrefixBonus = 30;
    private const int WordStartBonus = 20;
    private const int SubsequencePerChar = 10;
    private const int ConsecutiveBonus = 6;
    private const int SubsequenceWordStartBonus = 8;
    private const int MaxSpreadPerChar = 4;
    private const int MaxSpreadSlack = 4;

    /// <summary>
    /// A score (higher is better) when every space-separated word of <paramref name="query"/> matches
    /// <paramref name="text"/>, or null when any word doesn't. An empty query matches everything with score 0.
    /// </summary>
    public static int? Score(string? query, string text)
    {
        var words = (query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            return 0;
        }

        var haystack = text.ToLowerInvariant();
        var total = 0;
        foreach (var word in words)
        {
            if (ScoreWord(word.ToLowerInvariant(), haystack) is not { } score)
            {
                return null;
            }

            total += score;
        }

        return total;
    }

    private static int? ScoreWord(string word, string text)
    {
        // A contiguous match is always better than letters scattered through the text.
        var at = text.IndexOf(word, StringComparison.Ordinal);
        if (at >= 0)
        {
            var score = SubstringBase + (word.Length * SubstringPerChar);
            if (at == 0)
            {
                score += PrefixBonus;
            }
            else if (IsWordStart(text, at))
            {
                score += WordStartBonus;
            }

            return score - Math.Min(at, 15);
        }

        // Letters spread through the text only count as an abbreviation ("tlp" for "Toggle log panel"): the first letter must
        // begin a word, and the rest must follow reasonably close behind. Otherwise a long query would "match" almost
        // anything that happens to contain those letters somewhere.
        var first = FirstWordStartOf(word[0], text);
        if (first < 0)
        {
            return null;
        }

        var scattered = SubsequencePerChar + SubsequenceWordStartBonus;
        var last = first;
        var next = first + 1;
        for (var i = 1; i < word.Length; i++)
        {
            var found = text.IndexOf(word[i], next);
            if (found < 0)
            {
                return null;
            }

            scattered += SubsequencePerChar;
            if (found == last + 1)
            {
                scattered += ConsecutiveBonus;
            }

            if (IsWordStart(text, found))
            {
                scattered += SubsequenceWordStartBonus;
            }

            scattered -= Math.Min(found - next, 5);
            last = found;
            next = found + 1;
        }

        return last - first <= (word.Length * MaxSpreadPerChar) + MaxSpreadSlack ? Math.Max(1, scattered) : null;
    }

    private static int FirstWordStartOf(char letter, string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == letter && IsWordStart(text, i))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsWordStart(string text, int index) =>
        index == 0 || !char.IsLetterOrDigit(text[index - 1]);
}
