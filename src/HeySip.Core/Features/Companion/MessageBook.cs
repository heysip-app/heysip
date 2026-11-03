using HeySip.Core.Features.Scheduling;

namespace HeySip.Core.Features.Companion;

/// <summary>What the companion says. <c>{name}</c> is dropped cleanly (with its comma) when no name is set.</summary>
public sealed class MessageBook(Random random)
{
    private static readonly Dictionary<MessageStyle, string[][]> Prompts = new()
    {
        [MessageStyle.Friendly] =
        [
            ["{name}, time for a sip of water!", "Hydration check, {name}! 💧", "Psst {name}, a few sips? Stay glowing!"],
            ["Still thirsty over here, {name}!", "Your water bottle misses you, {name} 💧", "Just a quick sip, {name}? Pretty please!"],
            ["{name}, I'm not leaving until you drink!", "Okay {name}, this is a serious water emergency!", "Drink up, {name}! I'm very persistent 💦"],
        ],
        [MessageStyle.Coach] =
        [
            ["{name}, hydrate. Now.", "Water break, {name}. Champions drink.", "Drink up, {name}. No excuses!"],
            ["{name}, you skipped one. Don't skip two.", "Every sip counts, {name}. Get after it!", "Back on track, {name}. Drink!"],
            ["{name}! Drop everything and drink water!", "This is your coach speaking, {name}: WATER. NOW.", "No more delays, {name}. Hydrate!"],
        ],
        [MessageStyle.Zen] =
        [
            ["{name}, pause. Breathe. Sip.", "A quiet moment for water, {name}.", "Hydrate gently, {name}."],
            ["The water is patient, {name}. Will you visit it?", "A small sip restores balance, {name}.", "Return to the water, {name}."],
            ["{name}, the river calls. Drink.", "Balance is one glass away, {name}.", "Stillness begins with a sip, {name}."],
        ],
    };

    private static readonly Dictionary<MessageStyle, string[]> Cheers = new()
    {
        [MessageStyle.Friendly] = ["Yay {name}! Stay glowing ✨", "Amazing, {name}! Keep it up 💧", "Hydrated and happy, {name}! ✨"],
        [MessageStyle.Coach] = ["That's how it's done, {name}!", "One more rep in the bank, {name}! 💪", "Strong work, {name}!"],
        [MessageStyle.Zen] = ["Well done, {name}. Balance restored.", "Peace flows, {name}. 🌿", "Thank you for caring for yourself, {name}."],
    };

    private static readonly Dictionary<MessageStyle, string[]> Hellos = new()
    {
        [MessageStyle.Friendly] = ["Hi {name}! I'm your new buddy 👋", "Hello {name}! Nice to meet you ✨"],
        [MessageStyle.Coach] = ["Hey {name}! Ready when you are 💪", "Hi {name}! Let's get hydrated."],
        [MessageStyle.Zen] = ["Hello, {name}. I'll be here with you. 🌿", "Hi {name}. Glad to be here."],
    };

    private static readonly Dictionary<MessageStyle, string[]> NewLooks = new()
    {
        [MessageStyle.Friendly] = ["Ooh, a new look! How do I look, {name}? ✨", "Like my new style, {name}? 😄"],
        [MessageStyle.Coach] = ["New gear! Looking sharp, {name}? 💪", "Fresh look, {name}. Ready to go!"],
        [MessageStyle.Zen] = ["A small change, {name}. How does it feel? 🌿", "Something new, {name}. I like it."],
    };

    private static readonly Dictionary<MessageStyle, string[]> GoalLines = new()
    {
        [MessageStyle.Friendly] = ["Daily goal reached, {name}! 🎉", "You did it, {name}! Goal complete! 🎉"],
        [MessageStyle.Coach] = ["Goal crushed, {name}! 🏆", "Daily target hit, {name}. Take a bow."],
        [MessageStyle.Zen] = ["Today's goal is complete, {name}. 🌊", "You are fully hydrated, {name}."],
    };

    public string Prompt(MessageStyle style, string name, EscalationLevel level) =>
        Fill(Pick(Prompts[style][(int)level]), name);

    public string Cheer(MessageStyle style, string name) => Fill(Pick(Cheers[style]), name);

    public string GoalReached(MessageStyle style, string name) => Fill(Pick(GoalLines[style]), name);

    public string Snoozed(string name, int minutes) =>
        Fill($"I'll come back in {minutes} min, {{name}}!", name);

    /// <summary>A new character saying hi, with no question: what it looks like when it visits.</summary>
    public string Hello(MessageStyle style, string name) => Fill(Pick(Hellos[style]), name);

    /// <summary>The same character in a different outfit.</summary>
    public string NewLook(MessageStyle style, string name) => Fill(Pick(NewLooks[style]), name);

    public string Goodbye(string name) => Fill("See you in a bit, {name}! 👋", name);

    public static string Fill(string template, string? name)
    {
        var trimmed = (name ?? "").Trim();
        string result;
        if (trimmed.Length == 0)
        {
            result = template.Replace(", {name}", "").Replace("{name}, ", "").Replace(" {name}", "").Replace("{name}", "");
        }
        else
        {
            result = template.Replace("{name}", trimmed);
        }

        return result.Length == 0 ? result : char.ToUpperInvariant(result[0]) + result[1..];
    }

    private string Pick(string[] lines) => lines[random.Next(lines.Length)];
}
