using System;
using System.Collections.Generic;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using TaskImporter.Core;
using TaskImporterExtension.Commands;
using TaskImporterExtension.Services;

namespace TaskImporterExtension.Pages;

/// <summary>
/// The quick-add line. Entry is a line of text rather than a form, so the whole
/// page is really one item — the task about to be created — plus whatever the
/// parser wants to say about it.
///
/// Every keystroke re-parses. That is affordable because the parser resolves
/// names against a cached snapshot and never touches the network.
/// </summary>
internal sealed partial class AddTaskPage : DynamicListPage
{
    private const string Example = "Fix switch +Acme IT #onsite @friday 45m";

    /// <summary>
    /// How close together two requests for items count as the same visit, for
    /// the purpose of not re-reading the clipboard on every one of them.
    /// </summary>
    private static readonly TimeSpan SeedCooldown = TimeSpan.FromMilliseconds(500);

    private readonly TaskImporterHost _host;

    /// <summary>
    /// True on the clipboard-capture instance, which fills its own line when it
    /// is opened. The plain "Add task" instance leaves the line alone.
    /// </summary>
    private readonly bool _seedsFromClipboard;

    private IListItem[] _items;

    /// <summary>
    /// Link text from the clipboard's HTML flavour, when the line came from a
    /// capture. It describes one particular paste, so it survives only as long
    /// as that paste is still part of the line.
    /// </summary>
    private string? _anchorText;

    /// <summary>What was pasted, so the anchor text can be retired with it.</summary>
    private string? _capturedText;

    /// <summary>
    /// The text this page last wrote into its own line. While the line still
    /// says exactly this, nothing of the user's is at risk and the clipboard may
    /// be read over it.
    /// </summary>
    private string? _seededText;

    private DateTimeOffset _lastSeed = DateTimeOffset.MinValue;

    public AddTaskPage(TaskImporterHost host, bool seedsFromClipboard = false)
    {
        _host = host;
        _seedsFromClipboard = seedsFromClipboard;

        Icon = seedsFromClipboard ? Icons.Clipboard : Icons.Add;
        Title = seedsFromClipboard ? "Capture clipboard" : "Add task";
        Name = seedsFromClipboard ? "Capture" : "Add";
        PlaceholderText = Example;

        _items = Build(string.Empty);
    }

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        // Once the pasted text has been edited away, the anchor text describes
        // nothing in the line and would only mislabel whatever replaced it.
        if (_capturedText is not null && !newSearch.Contains(_capturedText, StringComparison.Ordinal))
        {
            _anchorText = null;
            _capturedText = null;
        }

        _items = Build(newSearch);
        RaiseItemsChanged(_items.Length);
    }

    public override IListItem[] GetItems()
    {
        if (_seedsFromClipboard) SeedFromClipboard();
        return _items;
    }

    /// <summary>
    /// Fills the line from the clipboard when this page is opened.
    ///
    /// There is no "navigated to" callback to hang this on, so it keys off the
    /// state of the line instead: the clipboard may overwrite an empty line, or
    /// one that still says exactly what this page last put there, and never
    /// anything the user has since typed. The cooldown is what stops a single
    /// visit — which asks for items several times over — from reading the
    /// clipboard once per ask.
    /// </summary>
    private void SeedFromClipboard()
    {
        var current = SearchText ?? string.Empty;

        var mine = current.Length == 0 ||
                   string.Equals(current, _seededText, StringComparison.Ordinal);

        if (!mine) return;

        var now = DateTimeOffset.UtcNow;
        if (now - _lastSeed < SeedCooldown) return;
        _lastSeed = now;

        var clipboard = WindowsClipboard.Read();
        if (!clipboard.HasText) return;

        // Re-reading the same clipboard onto the same line would only move the
        // caret about for no reason.
        if (string.Equals(clipboard.Text, current, StringComparison.Ordinal)) return;

        _anchorText = clipboard.AnchorText;
        _capturedText = clipboard.Text;
        _seededText = clipboard.Text;

        _items = Build(clipboard.Text);
        SearchText = clipboard.Text;
    }

    /// <summary>
    /// Empties the line after a successful submit, so the next visit starts
    /// clean and — on the capture page — picks up whatever is on the clipboard
    /// by then rather than the last thing that was sent.
    /// </summary>
    private void Clear()
    {
        _anchorText = null;
        _capturedText = null;
        _seededText = null;
        _lastSeed = DateTimeOffset.MinValue;

        _items = Build(string.Empty);
        SearchText = string.Empty;
    }

    /// <summary>
    /// Turns the current line into the list the user sees. Kept free of I/O —
    /// this runs on the keystroke path.
    /// </summary>
    private IListItem[] Build(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return [Hint(_seedsFromClipboard)];

        var context = _host.Capture(line, _anchorText);
        var parsed = context.Parsed;

        var items = new List<IListItem>(4)
        {
            new ListItem(new SubmitTaskCommand(_host, line, _anchorText, Clear))
            {
                Title = TitleFor(context),
                Subtitle = parsed.Describe(),
                Icon = context.Url is not null ? Icons.Link : Icons.Add,
                Tags = TagsFor(parsed),
            },
        };

        // Anything unresolved is reported rather than dropped: tags cannot be
        // created through the API, so a silent drop would lose the user's intent.
        if (parsed.UnresolvedProject is not null)
        {
            items.Add(Warning(
                $"No project called “{parsed.UnresolvedProject}”",
                "It stays in the title. Create the project in Super Productivity, or fix the spelling."));
        }

        foreach (var tag in parsed.UnresolvedTags)
        {
            items.Add(Warning(
                $"No tag called “{tag}”",
                "It stays in the title. Tags cannot be created through the API, so add it in Super Productivity first."));
        }

        return [.. items];
    }

    /// <summary>
    /// What the task will actually be called. For a bare link that is the page
    /// title, which is only fetched on submit — so until then it shows the
    /// clipboard's anchor text or a tidied URL.
    /// </summary>
    private static string TitleFor(CaptureContext context)
    {
        if (context.Parsed.TitleIsUrl)
        {
            var best = context.BestLinkTitle();
            if (!string.IsNullOrWhiteSpace(best)) return best!;
        }

        var title = context.Parsed.Title;
        return string.IsNullOrWhiteSpace(title) ? "(no title)" : title;
    }

    private static ITag[] TagsFor(ParseResult parsed)
    {
        if (parsed.ProjectName is null && parsed.TagNames.Count == 0) return [];

        var tags = new List<ITag>(1 + parsed.TagNames.Count);
        if (parsed.ProjectName is not null) tags.Add(new Tag(parsed.ProjectName));
        foreach (var name in parsed.TagNames) tags.Add(new Tag("#" + name));
        return [.. tags];
    }

    private static ListItem Hint(bool fromClipboard) => new(new NoOpCommand())
    {
        Title = fromClipboard ? "Nothing on the clipboard" : "Type a task",
        Subtitle = fromClipboard ? "Copy something, then reopen this. Or just type." : Example,
        Icon = Icons.Hint,
        Details = new Details
        {
            Title = "Quick-add syntax",
            Body = """
                | Token | Example | Effect |
                | --- | --- | --- |
                | `+project` | `+Acme IT` | Sets the project. No quoting needed. |
                | `#tag` | `#onsite` | Adds a tag. Repeatable. |
                | `@due` | `@friday`, `@oct5`, `@+3d`, `@eom` | Sets the due date. |
                | `@due time` | `@tomorrow 9am` | Sets a due date with a time. |
                | estimate | `45m`, `1h30m`, `1.5h` | Sets the time estimate. |
                | `//notes` | `// rack 3, APC 1500` | The rest becomes notes. |
                | a URL | pasted anywhere | Title from the page, link into the notes. |

                Tokens only count at a word boundary, so `C#` and `rob@example.com`
                are safe. A `\` before `+`, `#` or `@` makes it literal.
                """,
        },
    };

    private static ListItem Warning(string title, string subtitle) => new(new NoOpCommand())
    {
        Title = title,
        Subtitle = subtitle,
        Icon = Icons.Warning,
    };
}
