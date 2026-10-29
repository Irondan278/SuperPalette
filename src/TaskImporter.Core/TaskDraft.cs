using System;
using System.Collections.Generic;
using System.Linq;

namespace TaskImporter.Core;

/// <summary>
/// A task on its way to Super Productivity. Mutable on purpose: the parser
/// produces one, enrichers and routers shape it, and the REST client
/// serialises it. Maps one-to-one onto the POST /tasks body.
/// </summary>
public sealed class TaskDraft
{
    /// <summary>Super Productivity caps titles at 300 characters.</summary>
    public const int MaxTitleLength = 300;

    /// <summary>Super Productivity caps notes at 100,000 characters.</summary>
    public const int MaxNotesLength = 100_000;

    public string Title { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public string? ProjectId { get; set; }

    public IList<string> TagIds { get; } = new List<string>();

    /// <summary>A whole-day due date, serialised as yyyy-MM-dd.</summary>
    public DateOnly? DueDay { get; set; }

    /// <summary>A due date with a time of day. Wins over <see cref="DueDay"/>.</summary>
    public DateTimeOffset? DueWithTime { get; set; }

    /// <summary>Estimate in milliseconds, which is the unit the API expects.</summary>
    public long? TimeEstimateMs { get; set; }

    /// <summary>
    /// Fields the user set explicitly, by name. Routing rules consult this so
    /// they can fill gaps without overwriting anything that was typed.
    /// </summary>
    public ISet<string> ExplicitFields { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public bool WasSetExplicitly(string field) => ExplicitFields.Contains(field);

    public void MarkExplicit(string field) => ExplicitFields.Add(field);

    /// <summary>
    /// Adds a tag unless it is already present. Tag ids are opaque strings from
    /// the API, so comparison is ordinal.
    /// </summary>
    public void AddTag(string tagId)
    {
        if (string.IsNullOrWhiteSpace(tagId)) return;
        if (!TagIds.Contains(tagId, StringComparer.Ordinal)) TagIds.Add(tagId);
    }

    /// <summary>
    /// Prepends a line to the notes, keeping whatever was already there.
    /// Used to put a captured URL above the user's own text.
    /// </summary>
    public void PrependNoteLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        Notes = string.IsNullOrWhiteSpace(Notes) ? line : line + "\n\n" + Notes;
    }

    /// <summary>
    /// Trims the draft to the limits the API enforces, so a long fetched page
    /// title can never produce a rejected request.
    /// </summary>
    public TaskDraft Clamp()
    {
        Title = Truncate(Title.Trim(), MaxTitleLength);
        if (Notes is not null) Notes = Truncate(Notes.Trim(), MaxNotesLength);
        if (Notes?.Length == 0) Notes = null;
        return this;
    }

    private static string Truncate(string value, int max)
    {
        if (value.Length <= max) return value;
        // Leave room for the ellipsis and try not to cut mid-word.
        var cut = value.LastIndexOf(' ', Math.Min(max - 1, value.Length - 1));
        if (cut < max / 2) cut = max - 1;
        return value[..cut].TrimEnd() + "…";
    }
}
