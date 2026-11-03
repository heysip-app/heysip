using HeySip.Core.SharedKernel;

namespace HeySip.Tests.SharedKernel;

public sealed class FuzzyMatcherTests
{
    [Fact]
    public void An_empty_query_matches_everything_with_no_preference()
    {
        Assert.Equal(0, FuzzyMatcher.Score("", "Log a glass"));
        Assert.Equal(0, FuzzyMatcher.Score("   ", "Log a glass"));
        Assert.Equal(0, FuzzyMatcher.Score(null, "Log a glass"));
    }

    [Theory]
    [InlineData("xyz", "Log a glass")]
    [InlineData("glassx", "Log a glass")]
    [InlineData("log zebra", "Log a glass")] // every word must match
    [InlineData("longer than the text itself", "short")]
    public void Text_that_does_not_contain_the_query_does_not_match(string query, string text) =>
        Assert.Null(FuzzyMatcher.Score(query, text));

    [Fact]
    public void Matching_ignores_case()
    {
        Assert.NotNull(FuzzyMatcher.Score("LOG", "log a glass"));
        Assert.NotNull(FuzzyMatcher.Score("log", "LOG A GLASS"));
    }

    [Fact]
    public void Words_can_be_typed_in_any_order()
    {
        Assert.NotNull(FuzzyMatcher.Score("glass log", "Log a glass"));
    }

    [Fact]
    public void Letters_scattered_through_the_text_still_match_but_rank_below_a_real_substring()
    {
        var scattered = FuzzyMatcher.Score("lgg", "Log a glass");
        var substring = FuzzyMatcher.Score("log", "Log a glass");

        Assert.NotNull(scattered);
        Assert.True(substring > scattered);
    }

    [Fact]
    public void A_match_at_the_start_beats_one_at_a_word_start_beats_one_mid_word()
    {
        var prefix = FuzzyMatcher.Score("log", "Log a glass")!.Value;
        var wordStart = FuzzyMatcher.Score("log", "Open today log")!.Value;
        var midWord = FuzzyMatcher.Score("log", "Catalog entries")!.Value;

        Assert.True(prefix > wordStart);
        Assert.True(wordStart > midWord);
    }

    [Fact]
    public void An_earlier_match_beats_a_later_one()
    {
        Assert.True(FuzzyMatcher.Score("sip", "Take a sip now")! > FuzzyMatcher.Score("sip", "Take a very long moment before one sip")!);
    }

    [Fact]
    public void Ranking_a_realistic_command_list_puts_the_obvious_one_first()
    {
        var commands = new[] { "Log a glass", "Open today's log", "Toggle log panel", "Go to Settings", "Pause for 1 hour" };

        var ranked = commands
            .Select(text => (text, score: FuzzyMatcher.Score("log", text)))
            .Where(match => match.score.HasValue)
            .OrderByDescending(match => match.score)
            .Select(match => match.text)
            .ToList();

        Assert.Equal("Log a glass", ranked[0]);
        Assert.DoesNotContain("Go to Settings", ranked);
        Assert.DoesNotContain("Pause for 1 hour", ranked);
    }

    [Fact]
    public void A_word_start_match_beats_the_same_letters_in_the_middle_of_a_word()
    {
        Assert.True(FuzzyMatcher.Score("gl", "Log a glass")! > FuzzyMatcher.Score("gl", "Toggle log panel")!);
    }

    [Theory]
    [InlineData("interval", "Settings Delete drink history Remove everything that has been logged")]
    [InlineData("interval", "Settings Time zone Which clock to follow timezone travel local utc")]
    [InlineData("interval", "Settings Show a test reminder Preview what a reminder looks like preview try demo")]
    [InlineData("ogg", "Log a glass")] // the first letter must start a word
    public void Letters_that_merely_appear_somewhere_in_long_text_are_not_a_match(string query, string text) =>
        Assert.Null(FuzzyMatcher.Score(query, text));

    [Theory]
    [InlineData("lgg", "Log a glass")]
    [InlineData("tlp", "Toggle log panel")]
    [InlineData("gts", "Go to Settings")]
    public void Abbreviations_made_of_word_starts_still_match(string query, string text) =>
        Assert.NotNull(FuzzyMatcher.Score(query, text));
}
