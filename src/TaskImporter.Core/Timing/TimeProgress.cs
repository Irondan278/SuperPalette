using System;

namespace TaskImporter.Core.Timing;

/// <summary>
/// Time spent against an estimate, worded for a narrow strip of screen.
///
/// Super Productivity only reports time spent as of the moment it was asked, so
/// a running timer is counted forward locally between fetches. Both figures
/// arrive as milliseconds.
/// </summary>
public static class TimeProgress
{
    /// <summary>
    /// The time spent now, given what was reported at <paramref name="fetchedAt"/>.
    /// A stopped timer does not move; a clock that went backwards adds nothing.
    /// </summary>
    public static long Extrapolate(long spentMs, bool running, DateTimeOffset fetchedAt, DateTimeOffset now)
    {
        if (!running || now <= fetchedAt) return Math.Max(spentMs, 0);
        return Math.Max(spentMs, 0) + (long)(now - fetchedAt).TotalMilliseconds;
    }

    /// <summary>
    /// "42m / 1h" when stopped, "42:13 / 1h" when running — seconds only while
    /// they are actually ticking. Past the estimate, the overrun is spelled out.
    /// </summary>
    public static string Describe(long spentMs, long estimateMs, bool running)
    {
        var spent = running ? Clock(spentMs) : Humanise(spentMs);
        if (estimateMs <= 0) return spent;

        var text = $"{spent} / {Humanise(estimateMs)}";
        var over = spentMs - estimateMs;

        // Under a minute over reads as "0m over", which is noise.
        return over >= 60_000 ? $"{text} · {Humanise(over)} over" : text;
    }

    /// <summary>1h30m, 2h, 45m. Rounds down to the minute.</summary>
    public static string Humanise(long milliseconds)
    {
        if (milliseconds <= 0) return "0m";

        var span = TimeSpan.FromMilliseconds(milliseconds);
        var hours = (int)span.TotalHours;
        var minutes = span.Minutes;

        if (hours > 0 && minutes > 0) return $"{hours}h{minutes}m";
        return hours > 0 ? $"{hours}h" : $"{minutes}m";
    }

    /// <summary>42:07, 1:02:07. Hours appear only once there are some.</summary>
    public static string Clock(long milliseconds)
    {
        var span = TimeSpan.FromMilliseconds(Math.Max(milliseconds, 0));
        var hours = (int)span.TotalHours;

        return hours > 0
            ? $"{hours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes}:{span.Seconds:00}";
    }
}
