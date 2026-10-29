using System;
using System.Collections.Generic;

namespace TaskImporter.Core.Parsing;

/// <summary>An id and display name from Super Productivity.</summary>
public readonly record struct NamedItem(string Id, string Name);

/// <summary>
/// The projects and tags the parser resolves names against. A snapshot rather
/// than a live lookup so a parse can never block on the network — the palette
/// re-parses on every keystroke.
/// </summary>
public sealed class ReferenceSnapshot
{
    public static readonly ReferenceSnapshot Empty = new([], []);

    public ReferenceSnapshot(IReadOnlyList<NamedItem> projects, IReadOnlyList<NamedItem> tags)
    {
        Projects = projects;
        Tags = tags;
    }

    public IReadOnlyList<NamedItem> Projects { get; }

    public IReadOnlyList<NamedItem> Tags { get; }

    public NamedItem? FindProjectByName(string name)
        => Find(Projects, name);

    public NamedItem? FindTagByName(string name)
        => Find(Tags, name);

    private static NamedItem? Find(IReadOnlyList<NamedItem> items, string name)
    {
        foreach (var item in items)
        {
            if (string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)) return item;
        }

        return null;
    }
}
