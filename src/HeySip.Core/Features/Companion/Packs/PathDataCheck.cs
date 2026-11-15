using System.Globalization;

namespace HeySip.Core.Features.Companion.Packs;

/// <summary>Checks SVG path data against what packs may use: the commands M L H V C S Q T A Z, within size limits.</summary>
public static class PathDataCheck
{
    private const char MoveTo = 'M';
    private const char ArcTo = 'A';

    /// <summary>What <see cref="ArgumentCount"/> says for a letter that isn't a path command.</summary>
    private const int NotACommand = -1;

    // Arc arguments: rx ry rotation large-arc-flag sweep-flag x y. The two flags are a single 0 or 1.
    private const int ArcLargeArcFlagIndex = 3;
    private const int ArcSweepFlagIndex = 4;

    private const char Comma = ',';
    private const char DecimalPoint = '.';
    private const char FlagOff = '0';
    private const char FlagOn = '1';

    private static int ArgumentCount(char command) => char.ToUpperInvariant(command) switch
    {
        MoveTo or 'L' or 'T' => 2,
        'H' or 'V' => 1,
        'C' => 6,
        'S' or 'Q' => 4,
        ArcTo => 7,
        'Z' => 0,
        _ => NotACommand,
    };

    /// <summary>Returns null when the data is acceptable, otherwise a short reason.</summary>
    public static string? Validate(string data)
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            return "a path is empty";
        }

        if (data.Length > PackLimits.MaxPathChars)
        {
            return "a path is too long";
        }

        var position = 0;
        var segments = 0;
        var seenCommand = false;

        while (true)
        {
            SkipSeparators(data, ref position);
            if (position >= data.Length)
            {
                break;
            }

            var command = data[position];
            var count = ArgumentCount(command);
            if (count < 0)
            {
                return $"a path uses '{command}', which isn't allowed";
            }

            if (!seenCommand && char.ToUpperInvariant(command) != MoveTo)
            {
                return "a path must start with M";
            }

            seenCommand = true;
            position++;

            if (count == 0)
            {
                if (++segments > PackLimits.MaxPathSegments)
                {
                    return "a path has too many segments";
                }

                continue;
            }

            // A command may be followed by several sets of numbers (an implicit repeat).
            var sets = 0;
            while (true)
            {
                SkipSeparators(data, ref position);
                if (position >= data.Length || char.IsLetter(data[position]))
                {
                    break;
                }

                for (var i = 0; i < count; i++)
                {
                    SkipSeparators(data, ref position);
                    var isFlag = char.ToUpperInvariant(command) == ArcTo && (i == ArcLargeArcFlagIndex || i == ArcSweepFlagIndex);
                    if (!TryReadNumber(data, ref position, isFlag, out var value))
                    {
                        return "a path has a missing or malformed number";
                    }

                    if (Math.Abs(value) > PackLimits.MaxPathNumber)
                    {
                        return "a path has a number that is too large";
                    }
                }

                sets++;
                if (++segments > PackLimits.MaxPathSegments)
                {
                    return "a path has too many segments";
                }
            }

            if (sets == 0)
            {
                return $"a path command '{command}' has no numbers";
            }
        }

        return seenCommand ? null : "a path is empty";
    }

    private static void SkipSeparators(string data, ref int position)
    {
        while (position < data.Length && (char.IsWhiteSpace(data[position]) || data[position] == Comma))
        {
            position++;
        }
    }

    private static bool TryReadNumber(string data, ref int position, bool isFlag, out double value)
    {
        value = 0;
        if (isFlag)
        {
            // Arc flags are a single 0 or 1, and may be written without a separator after them.
            if (position < data.Length && (data[position] == FlagOff || data[position] == FlagOn))
            {
                value = data[position] - FlagOff;
                position++;
                return true;
            }

            return false;
        }

        var start = position;
        if (position < data.Length && (data[position] == '+' || data[position] == '-'))
        {
            position++;
        }

        var digits = 0;
        while (position < data.Length && char.IsAsciiDigit(data[position]))
        {
            position++;
            digits++;
        }

        if (position < data.Length && data[position] == DecimalPoint)
        {
            position++;
            while (position < data.Length && char.IsAsciiDigit(data[position]))
            {
                position++;
                digits++;
            }
        }

        if (digits == 0)
        {
            return false;
        }

        if (position < data.Length && (data[position] == 'e' || data[position] == 'E'))
        {
            position++;
            if (position < data.Length && (data[position] == '+' || data[position] == '-'))
            {
                position++;
            }

            var exponentDigits = 0;
            while (position < data.Length && char.IsAsciiDigit(data[position]))
            {
                position++;
                exponentDigits++;
            }

            if (exponentDigits == 0)
            {
                return false;
            }
        }

        return double.TryParse(data.AsSpan(start, position - start), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && double.IsFinite(value);
    }
}
