using HeySip.Core.Features.Companion;
using HeySip.Core.Features.Scheduling;

namespace HeySip.Tests.Features.Companion;

public sealed class MessageBookTests
{
    [Theory]
    [InlineData("{name}, time for a sip!", "Alex", "Alex, time for a sip!")]
    [InlineData("{name}, time for a sip!", "", "Time for a sip!")]
    [InlineData("Drink up, {name}!", "", "Drink up!")]
    [InlineData("Hydration check, {name}! 💧", "  ", "Hydration check! 💧")]
    public void The_name_is_inserted_or_dropped_cleanly(string template, string name, string expected) =>
        Assert.Equal(expected, MessageBook.Fill(template, name));

    [Theory]
    [InlineData(MessageStyle.Friendly)]
    [InlineData(MessageStyle.Coach)]
    [InlineData(MessageStyle.Zen)]
    public void Every_style_and_level_has_prompts_without_leftover_placeholders(MessageStyle style)
    {
        var book = new MessageBook(new Random(1));

        foreach (var level in Enum.GetValues<EscalationLevel>())
        {
            for (var i = 0; i < 20; i++)
            {
                Assert.DoesNotContain("{name}", book.Prompt(style, "", level));
                Assert.DoesNotContain("{name}", book.Prompt(style, "Sam", level));
            }
        }

        Assert.DoesNotContain("{", book.Cheer(style, ""));
        Assert.DoesNotContain("{", book.GoalReached(style, "Sam"));
    }

    [Fact]
    public void Snooze_message_mentions_the_minutes() =>
        Assert.Equal("I'll come back in 10 min, Alex!", new MessageBook(new Random(1)).Snoozed("Alex", 10));
}
