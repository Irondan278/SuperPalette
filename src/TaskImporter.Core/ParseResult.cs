using System;
using System.Collections.Generic;

namespace TaskImporter.Core;

/// <summary>Where in the raw text a recognised token was found.</summary>
/// <param name="Kind">The sort of token, e.g. "project", "tag", "due".</param>
/// <param name="Start">Index into the raw text.</param>
/// <param name="Length">Length in characters.</param>
/// <param name="Text">The matched text, verbatim.</param>
public readonly record struct TokenSpan(string Kind, int Start, int Length, string Text);

/// <summary>
/// What the quick-add parser made of one line of input. Carries the resolved
/// values, the things it could not resolve, and the spans it consumed.
/// The spans are unused in v1; they exist so token highlighting and
/// span-aware routing rules can be added without changing the parser.
/// </summary>
public sealed class ParseResult
{
    public string RawText { get; init; } = string.Empty;

    /// <summary>What is left after every recognised token was removed.</summary>
    public string Title { get; init; } = string.Empty;

    public string? Notes { get; init; }

    public string? ProjectId { get; init; }

    public string? ProjectName { get; init; }

    public IReadOnlyList<string> TagIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> TagNames { get; init; } = Array.Empty<string>();

    public DateOnly? DueDay { get; init; }

    public DateTimeOffset? DueWithTime { get; init; }

    public long? TimeEstimateMs { get; init; }

    /// <summary>The first URL found in the line, if any.</summary>
    public Uri? Url { get; init; }

    /// <summary>True when the line was nothing but a URL.</summary>
    public bool TitleIsUrl { get; init; }

    /// <summary>A +project that matched nothing. Left in the title as well.</summary>
    public string? UnresolvedProject { get; init; }

    /// <summary>#tags that matched nothing. Tags cannot be created via the API.</summary>
    public IReadOnlyList<string> UnresolvedTags { get; init; } = Array.Empty<string>();

    public IReadOnlyList<TokenSpan> Spans { get; init; } = Array.Empty<TokenSpan>();

    public bool HasWarnings => UnresolvedProject is not null || UnresolvedTags.Count > 0;

    /// <summary>
    /// Turns the parse into a draft, marking every field the user set explicitly
    /// so routing rules know not to overwrite them.
    /// </summary>
    public TaskDraft ToDraft()
    {
        var draft = new TaskDraft { Title = Title, Notes = Notes };

        if (Notes is not null) draft.MarkExplicit(nameof(TaskDraft.Notes));

        if (ProjectId is not null)
        {
            draft.ProjectId = ProjectId;
            draft.MarkExplicit(nameof(TaskDraft.ProjectId));
        }

        foreach (var tagId in TagIds) draft.AddTag(tagId);
        if (TagIds.Count > 0) draft.MarkExplicit(nameof(TaskDraft.TagIds));

        if (DueWithTime is not null)
        {
            draft.DueWithTime = DueWithTime;
            draft.MarkExplicit(nameof(TaskDraft.DueWithTime));
        }
        else if (DueDay is not null)
        {
            draft.DueDay = DueDay;
            draft.MarkExplicit(nameof(TaskDraft.DueDay));
        }

        if (TimeEstimateMs is not null)
        {
            draft.TimeEstimateMs = TimeEstimateMs;
            draft.MarkExplicit(nameof(TaskDraft.TimeEstimateMs));
        }

        return draft;
    }

    /// <summary>
    /// A one-line summary for the palette subtitle, so the line explains itself
    /// before Enter is pressed.
    /// </summary>
    public string Describe()
    {
        var parts = new List<string>();

        if (ProjectName is not null) parts.Add(ProjectName);
        foreach (var tag in TagNames) parts.Add("#" + tag);

        if (DueWithTime is not null) parts.Add(DueWithTime.Value.ToString("ddd MMM d, h:mm tt"));
        else if (DueDay is not null) parts.Add(DueDay.Value.ToString("ddd MMM d"));

        if (TimeEstimateMs is not null) parts.Add(FormatEstimate(TimeEstimateMs.Value));
        if (Notes is not null) parts.Add("+notes");

        if (UnresolvedProject is not null) parts.Add($"no project “{UnresolvedProject}”");
        foreach (var tag in UnresolvedTags) parts.Add($"no tag “{tag}”");

        return parts.Count == 0 ? "No project, no due date" : string.Join(" · ", parts);
    }

    private static string FormatEstimate(long ms)
    {
        var span = TimeSpan.FromMilliseconds(ms);
        var hours = (int)span.TotalHours;
        var minutes = span.Minutes;
        if (hours > 0 && minutes > 0) return $"{hours}h{minutes}m";
        if (hours > 0) return $"{hours}h";
        return $"{minutes}m";
    }
}
