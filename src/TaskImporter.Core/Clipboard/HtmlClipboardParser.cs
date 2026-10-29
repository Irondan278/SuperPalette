using System;
using System.Text.RegularExpressions;

namespace TaskImporter.Core.Clipboard;

/// <summary>
/// The anchor text and href pulled out of the clipboard's HTML flavour.
/// </summary>
/// <param name="Href">The link target, if a single anchor was found.</param>
/// <param name="Text">The link's visible text, decoded and whitespace-collapsed.</param>
public readonly record struct ClipboardLink(string? Href, string? Text)
{
    public bool HasText => !string.IsNullOrWhiteSpace(Text);
}

/// <summary>
/// Parses the Windows CF_HTML clipboard format.
///
/// When a link is copied from a web page, Windows puts an HTML fragment on the
/// clipboard alongside the plain text, and that fragment contains the anchor
/// with its visible text. Reading it costs nothing and needs no network, which
/// is why it is tried before fetching the page.
///
/// This is deliberately pure string work with no Windows dependency, so it can
/// be tested anywhere. The Windows-specific part — asking the clipboard for the
/// "HTML Format" flavour — lives in the extension project.
/// </summary>
public static partial class HtmlClipboardParser
{
    [GeneratedRegex(@"<a\b[^>]*?\bhref\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))[^>]*>(.*?)</a\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex AnchorPattern();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.Singleline)]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"\s+", RegexOptions.None)]
    private static partial Regex WhitespacePattern();

    /// <summary>
    /// Extracts the single anchor from a CF_HTML payload. Returns an empty
    /// result when the fragment holds no anchor, or more than one — a multi-link
    /// selection is not a link the user meant to capture.
    /// </summary>
    public static ClipboardLink Parse(string? cfHtml)
    {
        if (string.IsNullOrWhiteSpace(cfHtml)) return default;

        var fragment = ExtractFragment(cfHtml!);
        var matches = AnchorPattern().Matches(fragment);
        if (matches.Count != 1) return default;

        var match = matches[0];
        var href = FirstNonEmpty(match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value);
        var text = CleanText(match.Groups[4].Value);

        return new ClipboardLink(
            string.IsNullOrWhiteSpace(href) ? null : HtmlText.Decode(href).Trim(),
            string.IsNullOrWhiteSpace(text) ? null : text);
    }

    /// <summary>
    /// CF_HTML starts with a header of Key:Value lines, including StartFragment
    /// and EndFragment byte offsets. Those offsets are into the UTF-8 bytes, not
    /// the characters, so rather than trusting them this reads the
    /// &lt;!--StartFragment--&gt; markers, falling back to the whole payload.
    /// </summary>
    public static string ExtractFragment(string cfHtml)
    {
        const string startMarker = "<!--StartFragment-->";
        const string endMarker = "<!--EndFragment-->";

        var start = cfHtml.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase);
        var end = cfHtml.IndexOf(endMarker, StringComparison.OrdinalIgnoreCase);

        if (start >= 0)
        {
            start += startMarker.Length;
            if (end > start) return cfHtml[start..end];
            return cfHtml[start..];
        }

        // No markers: strip the header, which ends at the first '<'.
        var firstTag = cfHtml.IndexOf('<');
        return firstTag > 0 ? cfHtml[firstTag..] : cfHtml;
    }

    private static string CleanText(string html)
    {
        var withoutTags = TagPattern().Replace(html, " ");
        var decoded = HtmlText.Decode(withoutTags);
        return WhitespacePattern().Replace(decoded, " ").Trim();
    }

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrEmpty(value)) return value;
        }

        return string.Empty;
    }
}
