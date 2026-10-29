using System;
using System.Linq;
using TaskImporter.Core.Enrichment;

namespace TaskImporter.Core;

/// <summary>
/// Turns a capture into the draft that will be submitted, applying the link
/// rules: a captured URL becomes a readable title and the raw link moves to the
/// first line of the notes.
///
/// Kept separate from both the parser and the router so the link behaviour has
/// one home and one set of tests.
/// </summary>
public static class DraftFactory
{
    public static TaskDraft Build(CaptureContext context)
    {
        var draft = context.Parsed.ToDraft();

        if (context.Url is not null)
        {
            var url = context.Url;
            var link = url.ToString();

            if (context.Parsed.TitleIsUrl)
            {
                // The whole line was a link: name the task after the page.
                var title = context.BestLinkTitle();
                if (!string.IsNullOrWhiteSpace(title)) draft.Title = title!;
            }
            else
            {
                // A link inside a longer line: keep the words, move the link out.
                var stripped = RemoveToken(draft.Title, link);
                if (stripped.Length > 0) draft.Title = stripped;
            }

            draft.PrependNoteLine(link);
        }

        return draft.Clamp();
    }

    private static string RemoveToken(string title, string token)
    {
        var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => !word.TrimEnd('.', ',', ')').Equals(token, StringComparison.Ordinal) &&
                           !word.Equals(token, StringComparison.Ordinal));

        return string.Join(' ', words).Trim();
    }

    /// <summary>
    /// Builds a capture from raw text plus whatever the clipboard offered,
    /// running the parser over it. The palette calls this on every keystroke.
    /// </summary>
    public static CaptureContext Capture(
        string rawText,
        Parsing.QuickAddParser parser,
        string? anchorText = null,
        string? sourceApp = null)
    {
        var parsed = parser.Parse(rawText);
        var kind = parsed.Url is not null
            ? CaptureKind.Url
            : CaptureContext.DetectKind(parsed.Title, out _);

        return new CaptureContext
        {
            RawText = rawText,
            Kind = kind,
            Url = parsed.Url,
            AnchorText = anchorText,
            SourceApp = sourceApp,
            Parsed = parsed,
        };
    }
}
