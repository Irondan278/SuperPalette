using System;
using System.Globalization;

namespace TaskImporter.Core.Parsing;

/// <summary>
/// Parses estimate tokens: 45m, 2h, 1h30m, 90min, 1.5h, 30 (bare minutes are
/// deliberately NOT accepted — too easy to eat part of a title).
/// </summary>
public static class DurationGrammar
{
    public static bool TryParse(string token, out long milliseconds)
    {
        milliseconds = 0;
        if (string.IsNullOrWhiteSpace(token)) return false;

        var text = token.Trim().ToLowerInvariant();
        var index = 0;
        var total = 0d;
        var matchedAny = false;

        while (index < text.Length)
        {
            var start = index;
            while (index < text.Length &&
                   (char.IsDigit(text[index]) || text[index] == '.' || text[index] == ','))
            {
                index++;
            }

            if (index == start) return false; // A unit with no number in front of it.

            var numberText = text[start..index].Replace(',', '.');
            if (!double.TryParse(numberText, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                return false;
            }

            var unitStart = index;
            while (index < text.Length && char.IsLetter(text[index])) index++;
            var unit = text[unitStart..index];

            var factor = unit switch
            {
                "h" or "hr" or "hrs" or "hour" or "hours" => 3_600_000d,
                "m" or "min" or "mins" or "minute" or "minutes" => 60_000d,
                "d" or "day" or "days" => 8 * 3_600_000d, // A working day, not 24h.
                _ => -1d,
            };

            if (factor < 0) return false;

            total += number * factor;
            matchedAny = true;
        }

        if (!matchedAny || total <= 0) return false;

        milliseconds = (long)Math.Round(total);
        return true;
    }
}
