using System;

namespace TaskImporter.Core;

/// <summary>
/// Everything known about one capture: what the user gave us, what was
/// detected about it, and where it came from.
///
/// This is deliberately richer than v1 needs. It is the input to
/// <see cref="Routing.ITaskRouter"/>, so every field a future routing rule
/// might want to match on belongs here rather than being plumbed through
/// later.
/// </summary>
public sealed class CaptureContext
{
    public required string RawText { get; init; }

    public CaptureKind Kind { get; init; } = CaptureKind.PlainText;

    /// <summary>Parsed once, so rules can read Host, AbsolutePath and Query.</summary>
    public Uri? Url { get; init; }

    /// <summary>The page title, once an enricher has resolved it.</summary>
    public string? PageTitle { get; set; }

    /// <summary>Link text from the clipboard's HTML flavour, when present.</summary>
    public string? AnchorText { get; init; }

    /// <summary>Process name of the foreground window at capture time.</summary>
    public string? SourceApp { get; init; }

    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.Now;

    /// <summary>What the quick-add syntax explicitly said.</summary>
    public required ParseResult Parsed { get; init; }

    /// <summary>
    /// The best title available for a captured link, in order of preference:
    /// the fetched page title, the clipboard's anchor text, then a tidied
    /// version of the URL itself.
    /// </summary>
    public string? BestLinkTitle()
    {
        if (!string.IsNullOrWhiteSpace(PageTitle)) return PageTitle!.Trim();
        if (!string.IsNullOrWhiteSpace(AnchorText)) return AnchorText!.Trim();
        return Url is null ? null : DescribeUrl(Url);
    }

    /// <summary>
    /// Last resort title for a link: host plus a cleaned final path segment,
    /// e.g. "github.com — super productivity".
    /// </summary>
    public static string DescribeUrl(Uri url)
    {
        var host = url.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            ? url.Host[4..]
            : url.Host;

        var segments = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return host;

        var last = Uri.UnescapeDataString(segments[^1]);

        // Drop a file extension, then turn separators into spaces.
        var dot = last.LastIndexOf('.');
        if (dot > 0 && last.Length - dot <= 6) last = last[..dot];
        last = last.Replace('-', ' ').Replace('_', ' ').Trim();

        return last.Length == 0 ? host : $"{host} — {last}";
    }

    public static CaptureKind DetectKind(string text, out Uri? url)
    {
        url = null;
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return CaptureKind.PlainText;

        if ((trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
             trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) &&
            Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed) &&
            !trimmed.Contains(' '))
        {
            url = parsed;
            return CaptureKind.Url;
        }

        if (trimmed.StartsWith(@"\\", StringComparison.Ordinal)) return CaptureKind.FilePath;

        if (trimmed.Length > 3 && char.IsLetter(trimmed[0]) && trimmed[1] == ':' &&
            (trimmed[2] == '\\' || trimmed[2] == '/'))
        {
            return CaptureKind.FilePath;
        }

        if (!trimmed.Contains(' ') && trimmed.Contains('@') && trimmed.Contains('.') &&
            trimmed.IndexOf('@') > 0 && trimmed.LastIndexOf('.') > trimmed.IndexOf('@'))
        {
            return CaptureKind.Email;
        }

        return CaptureKind.PlainText;
    }
}
