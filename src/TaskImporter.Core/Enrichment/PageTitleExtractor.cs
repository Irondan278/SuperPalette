using System;
using System.Text.RegularExpressions;
using TaskImporter.Core.Clipboard;

namespace TaskImporter.Core.Enrichment;

/// <summary>
/// Pulls a usable title out of a page's HTML.
///
/// Prefers og:title, which is usually the clean headline, and falls back to
/// &lt;title&gt;, which often carries a site-name suffix. Pure string work so it
/// is testable without a network.
/// </summary>
public static partial class PageTitleExtractor
{
    [GeneratedRegex(@"<title\b[^>]*>(.*?)</title\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleTag();

    [GeneratedRegex(
        @"<meta\b[^>]*?(?:property|name)\s*=\s*[""']og:title[""'][^>]*?content\s*=\s*""([^""]*)""",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex OgTitleContentLast();

    [GeneratedRegex(
        @"<meta\b[^>]*?content\s*=\s*""([^""]*)""[^>]*?(?:property|name)\s*=\s*[""']og:title[""']",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex OgTitleContentFirst();

    [GeneratedRegex(@"\s+", RegexOptions.None)]
    private static partial Regex Whitespace();

    public static string? Extract(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;

        var og = OgTitleContentLast().Match(html!);
        if (!og.Success) og = OgTitleContentFirst().Match(html!);
        if (og.Success)
        {
            var value = Clean(og.Groups[1].Value);
            if (value is not null) return value;
        }

        var title = TitleTag().Match(html!);
        if (title.Success) return Clean(title.Groups[1].Value);

        return null;
    }

    /// <summary>
    /// Removes a trailing site name, e.g. "Zebra TC53 | Zebra" becomes
    /// "Zebra TC53". Only trims when the tail is short and the head survives,
    /// so legitimate titles containing a dash are left alone.
    /// </summary>
    public static string TrimSiteSuffix(string title, string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return title;

        var bareHost = host!.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            ? host[4..]
            : host;

        var dot = bareHost.IndexOf('.');
        var siteName = dot > 0 ? bareHost[..dot] : bareHost;
        if (siteName.Length < 3) return title;

        foreach (var separator in new[] { " | ", " - ", " – ", " — ", " :: ", " · " })
        {
            var index = title.LastIndexOf(separator, StringComparison.Ordinal);
            if (index <= 0) continue;

            var tail = title[(index + separator.Length)..].Trim();
            if (tail.Length == 0 || tail.Length > 30) continue;
            if (!tail.Replace(" ", "").Equals(siteName, StringComparison.OrdinalIgnoreCase)) continue;

            var head = title[..index].Trim();
            if (head.Length >= 3) return head;
        }

        return title;
    }

    private static string? Clean(string value)
    {
        var decoded = HtmlText.Decode(value);
        var collapsed = Whitespace().Replace(decoded, " ").Trim();
        return collapsed.Length == 0 ? null : collapsed;
    }
}
