using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TaskImporter.Core.Parsing;

/// <summary>
/// Parses one quick-add line into a <see cref="ParseResult"/>.
///
/// Runs on every keystroke, so it does no I/O and allocates modestly. The
/// rules are documented in the build plan; the short version is that tokens
/// only count at a word boundary, unresolvable tokens stay in the title with a
/// warning rather than vanishing, and a leading backslash escapes anything.
/// </summary>
public sealed partial class QuickAddParser
{
    private readonly ReferenceSnapshot _reference;
    private readonly Func<DateOnly> _today;

    public QuickAddParser(ReferenceSnapshot? reference = null, Func<DateOnly>? today = null)
    {
        _reference = reference ?? ReferenceSnapshot.Empty;
        _today = today ?? (static () => DateOnly.FromDateTime(DateTime.Now));
    }

    [GeneratedRegex(@"(?<=^|\s)//", RegexOptions.None)]
    private static partial Regex NotesSeparator();

    public ParseResult Parse(string? input)
    {
        var raw = input ?? string.Empty;
        if (raw.Trim().Length == 0) return new ParseResult { RawText = raw };

        var today = _today();
        var spans = new List<TokenSpan>();

        // 1. Split the notes off first, so nothing after // is treated as syntax.
        var body = raw;
        string? notes = null;
        var notesMatch = NotesSeparator().Match(raw);
        if (notesMatch.Success)
        {
            body = raw[..notesMatch.Index];
            var text = raw[(notesMatch.Index + 2)..].Trim();
            if (text.Length > 0)
            {
                notes = text;
                spans.Add(new TokenSpan("notes", notesMatch.Index, raw.Length - notesMatch.Index,
                    raw[notesMatch.Index..]));
            }
        }

        var words = Tokenize(body);

        string? projectId = null, projectName = null, unresolvedProject = null;
        var tagIds = new List<string>();
        var tagNames = new List<string>();
        var unresolvedTags = new List<string>();
        DateOnly? dueDay = null;
        TimeOnly? dueTime = null;
        long? estimate = null;
        var titleWords = new List<string>();

        for (var i = 0; i < words.Count; i++)
        {
            var word = words[i];
            var text = word.Text;

            // Escape: a backslash before a sigil makes it literal. Restricted to
            // the sigils so that Windows paths — \\server\share, C:\dir — survive
            // untouched, which matters because pasted paths are a common capture.
            if (text.Length > 1 && text[0] == '\\' && IsSigil(text[1]))
            {
                titleWords.Add(text[1..]);
                continue;
            }

            // +project — greedy, so multi-word project names need no quoting.
            if (text.Length > 1 && text[0] == '+' && projectId is null && unresolvedProject is null)
            {
                var remainder = body[(word.Start + 1)..];
                if (TryMatchLongestName(remainder, _reference.Projects, out var project, out var matched))
                {
                    projectId = project.Id;
                    projectName = project.Name;
                    spans.Add(new TokenSpan("project", word.Start, matched + 1,
                        body.Substring(word.Start, matched + 1)));
                    i = SkipConsumedWords(words, i, word.Start + 1 + matched);
                    continue;
                }

                unresolvedProject = text[1..];
                titleWords.Add(text);
                continue;
            }

            // #tag — greedy too, because tag names may contain spaces.
            if (text.Length > 1 && text[0] == '#')
            {
                var remainder = body[(word.Start + 1)..];
                if (TryMatchLongestName(remainder, _reference.Tags, out var tag, out var matched))
                {
                    if (!tagIds.Contains(tag.Id, StringComparer.Ordinal))
                    {
                        tagIds.Add(tag.Id);
                        tagNames.Add(tag.Name);
                    }

                    spans.Add(new TokenSpan("tag", word.Start, matched + 1,
                        body.Substring(word.Start, matched + 1)));
                    i = SkipConsumedWords(words, i, word.Start + 1 + matched);
                    continue;
                }

                unresolvedTags.Add(text[1..]);
                titleWords.Add(text);
                continue;
            }

            // @due, optionally followed by a time.
            if (text.Length > 1 && text[0] == '@' && dueDay is null)
            {
                if (DateGrammar.TryParseDate(text[1..], today, out var parsedDate))
                {
                    dueDay = parsedDate;
                    spans.Add(new TokenSpan("due", word.Start, text.Length, text));

                    if (i + 1 < words.Count &&
                        DateGrammar.TryParseTime(words[i + 1].Text, out var parsedTime))
                    {
                        dueTime = parsedTime;
                        spans.Add(new TokenSpan("time", words[i + 1].Start,
                            words[i + 1].Text.Length, words[i + 1].Text));
                        i++;
                    }

                    continue;
                }

                titleWords.Add(text);
                continue;
            }

            // Estimate — only the first one counts.
            if (estimate is null && DurationGrammar.TryParse(text, out var ms))
            {
                estimate = ms;
                spans.Add(new TokenSpan("estimate", word.Start, text.Length, text));
                continue;
            }

            titleWords.Add(text);
        }

        var title = string.Join(' ', titleWords).Trim();

        // URL detection runs on what survived, so a link in the notes is ignored.
        Uri? url = null;
        var titleIsUrl = false;
        foreach (var candidate in titleWords)
        {
            if (!LooksLikeUrl(candidate)) continue;
            if (!Uri.TryCreate(candidate.TrimEnd('.', ',', ')'), UriKind.Absolute, out var parsed)) continue;

            url = parsed;
            titleIsUrl = titleWords.Count == 1;
            break;
        }

        return new ParseResult
        {
            RawText = raw,
            Title = title,
            Notes = notes,
            ProjectId = projectId,
            ProjectName = projectName,
            TagIds = tagIds,
            TagNames = tagNames,
            DueDay = dueTime is null ? dueDay : null,
            DueWithTime = dueTime is null || dueDay is null
                ? null
                // Unspecified kind means "local", which is what the user meant.
                : new DateTimeOffset(dueDay.Value.ToDateTime(dueTime.Value)),
            TimeEstimateMs = estimate,
            Url = url,
            TitleIsUrl = titleIsUrl,
            UnresolvedProject = unresolvedProject,
            UnresolvedTags = unresolvedTags,
            Spans = spans,
        };
    }

    // A backslash before anything else is just a backslash, so "\\server\share"
    // and "C:\dir" pass through whole.
    private static bool IsSigil(char c) => c is '+' or '#' or '@';

    private static bool LooksLikeUrl(string text)
        => text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
           text.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Finds the longest known name that the remainder starts with, stopping at
    /// a word boundary so "+Acme" cannot match a project called "Acme IT".
    /// </summary>
    private static bool TryMatchLongestName(
        string remainder,
        IReadOnlyList<NamedItem> candidates,
        out NamedItem match,
        out int matchedLength)
    {
        match = default;
        matchedLength = 0;

        foreach (var candidate in candidates)
        {
            var name = candidate.Name;
            if (name.Length == 0 || name.Length > remainder.Length) continue;
            if (!remainder.StartsWith(name, StringComparison.OrdinalIgnoreCase)) continue;

            // The match must end at a word boundary.
            if (remainder.Length > name.Length && !char.IsWhiteSpace(remainder[name.Length])) continue;

            if (name.Length > matchedLength)
            {
                match = candidate;
                matchedLength = name.Length;
            }
        }

        return matchedLength > 0;
    }

    /// <summary>
    /// Advances the word index past everything a greedy match consumed.
    /// Returns the index of the last consumed word, for a for-loop to increment.
    /// </summary>
    private static int SkipConsumedWords(IReadOnlyList<Word> words, int current, int consumedEnd)
    {
        var index = current;
        while (index + 1 < words.Count && words[index + 1].Start < consumedEnd) index++;
        return index;
    }

    private readonly record struct Word(string Text, int Start);

    private static List<Word> Tokenize(string text)
    {
        var words = new List<Word>();
        var index = 0;

        while (index < text.Length)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
            if (index >= text.Length) break;

            var start = index;
            while (index < text.Length && !char.IsWhiteSpace(text[index])) index++;
            words.Add(new Word(text[start..index], start));
        }

        return words;
    }
}
