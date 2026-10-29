using System;
using System.Globalization;

namespace TaskImporter.Core.Parsing;

/// <summary>
/// Parses the token after an @ into a date, and an optional following token
/// into a time of day.
///
/// Supported: today, tomorrow, tom, tmr, weekday names and their three-letter
/// forms, +3d / 3d / +2w / 2w, eow, eom, yyyy-MM-dd, MM-dd, and month-day
/// forms such as oct5 or 5oct.
///
/// Everything is resolved relative to a supplied "today" so the behaviour is
/// testable and does not drift with the clock during a parse.
/// </summary>
public static class DateGrammar
{
    public static bool TryParseDate(string token, DateOnly today, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(token)) return false;

        var text = token.Trim().ToLowerInvariant().TrimEnd('.', ',');
        if (text.Length == 0) return false;

        switch (text)
        {
            case "today" or "tod" or "now":
                date = today;
                return true;
            case "tomorrow" or "tom" or "tmr" or "tmrw":
                date = today.AddDays(1);
                return true;
            case "yesterday" or "yest":
                date = today.AddDays(-1);
                return true;
            case "eow": // End of the working week.
                date = NextWeekday(today, DayOfWeek.Friday, includeToday: true);
                return true;
            case "eom":
                date = new DateOnly(today.Year, today.Month,
                    DateTime.DaysInMonth(today.Year, today.Month));
                return true;
            case "weekend" or "sat":
                date = NextWeekday(today, DayOfWeek.Saturday, includeToday: true);
                return true;
        }

        if (TryParseWeekday(text, out var weekday))
        {
            // "@friday" on a Friday means today, which is what people mean when
            // they glance at the calendar and type the day they are looking at.
            date = NextWeekday(today, weekday, includeToday: true);
            return true;
        }

        if (TryParseRelative(text, today, out date)) return true;

        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out date))
        {
            return true;
        }

        if (TryParseMonthDay(text, today, out date)) return true;

        return false;
    }

    /// <summary>
    /// Parses 9am, 9:30pm, 14:30, 1430. Returns false for anything else so the
    /// caller can leave the token in the title.
    /// </summary>
    public static bool TryParseTime(string token, out TimeOnly time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(token)) return false;

        var text = token.Trim().ToLowerInvariant();
        var meridiem = 0; // 1 = am, 2 = pm

        if (text.EndsWith("am", StringComparison.Ordinal))
        {
            meridiem = 1;
            text = text[..^2].Trim();
        }
        else if (text.EndsWith("pm", StringComparison.Ordinal))
        {
            meridiem = 2;
            text = text[..^2].Trim();
        }

        if (text.Length == 0) return false;

        int hour, minute = 0;

        var colon = text.IndexOf(':');
        if (colon > 0)
        {
            if (!int.TryParse(text[..colon], out hour)) return false;
            if (!int.TryParse(text[(colon + 1)..], out minute)) return false;
        }
        else if (text.Length == 4 && int.TryParse(text, out var packed) && meridiem == 0)
        {
            hour = packed / 100;
            minute = packed % 100;
        }
        else if (int.TryParse(text, out hour))
        {
            // A bare number only counts as a time when am/pm said so.
            if (meridiem == 0) return false;
        }
        else
        {
            return false;
        }

        if (meridiem == 1 && hour == 12) hour = 0;
        else if (meridiem == 2 && hour < 12) hour += 12;

        if (hour is < 0 or > 23 || minute is < 0 or > 59) return false;

        time = new TimeOnly(hour, minute);
        return true;
    }

    private static bool TryParseWeekday(string text, out DayOfWeek day)
    {
        day = default;
        switch (text)
        {
            case "mon" or "monday": day = DayOfWeek.Monday; return true;
            case "tue" or "tues" or "tuesday": day = DayOfWeek.Tuesday; return true;
            case "wed" or "weds" or "wednesday": day = DayOfWeek.Wednesday; return true;
            case "thu" or "thur" or "thurs" or "thursday": day = DayOfWeek.Thursday; return true;
            case "fri" or "friday": day = DayOfWeek.Friday; return true;
            case "saturday": day = DayOfWeek.Saturday; return true;
            case "sun" or "sunday": day = DayOfWeek.Sunday; return true;
            default: return false;
        }
    }

    private static DateOnly NextWeekday(DateOnly from, DayOfWeek target, bool includeToday)
    {
        var delta = ((int)target - (int)from.DayOfWeek + 7) % 7;
        if (delta == 0 && !includeToday) delta = 7;
        return from.AddDays(delta);
    }

    private static bool TryParseRelative(string text, DateOnly today, out DateOnly date)
    {
        date = default;
        var body = text.StartsWith('+') ? text[1..] : text;
        if (body.Length < 2) return false;

        var unit = body[^1];
        if (unit is not ('d' or 'w' or 'm')) return false;

        if (!int.TryParse(body[..^1], out var count)) return false;

        date = unit switch
        {
            'd' => today.AddDays(count),
            'w' => today.AddDays(count * 7),
            _ => today.AddMonths(count),
        };

        return true;
    }

    private static readonly string[] MonthNames =
    [
        "jan", "feb", "mar", "apr", "may", "jun",
        "jul", "aug", "sep", "oct", "nov", "dec",
    ];

    private static bool TryParseMonthDay(string text, DateOnly today, out DateOnly date)
    {
        date = default;

        // MM-dd or MM/dd
        var separator = text.IndexOfAny(['-', '/']);
        if (separator > 0)
        {
            if (int.TryParse(text[..separator], out var m) &&
                int.TryParse(text[(separator + 1)..], out var d) &&
                m is >= 1 and <= 12 && d >= 1 && d <= DateTime.DaysInMonth(today.Year, m))
            {
                date = RollForward(new DateOnly(today.Year, m, d), today);
                return true;
            }

            return false;
        }

        // oct5 or 5oct
        for (var i = 0; i < MonthNames.Length; i++)
        {
            var name = MonthNames[i];
            string? dayPart = null;

            if (text.StartsWith(name, StringComparison.Ordinal)) dayPart = text[name.Length..];
            else if (text.EndsWith(name, StringComparison.Ordinal)) dayPart = text[..^name.Length];
            else continue;

            dayPart = dayPart.TrimEnd('s', 't', 'n', 'd', 'r', 'h'); // 1st, 2nd, 3rd, 4th
            if (dayPart.Length == 0) continue;
            if (!int.TryParse(dayPart, out var day)) continue;

            var month = i + 1;
            if (day < 1 || day > DateTime.DaysInMonth(today.Year, month)) continue;

            date = RollForward(new DateOnly(today.Year, month, day), today);
            return true;
        }

        return false;
    }

    /// <summary>A bare month and day that has already passed means next year.</summary>
    private static DateOnly RollForward(DateOnly candidate, DateOnly today)
        => candidate < today ? candidate.AddYears(1) : candidate;
}
