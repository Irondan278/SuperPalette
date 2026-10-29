using System;
using System.Collections.Generic;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using TaskImporter.Core.Timing;
using TaskImporter.SuperProductivity.Models;
using TaskImporterExtension.Commands;
using TaskImporterExtension.Services;

namespace TaskImporterExtension.Pages;

/// <summary>
/// What the timer is doing, and today's list underneath it.
///
/// Unlike the quick-add page this one does talk to the network, so it fetches
/// on open and after each action rather than on every keystroke.
/// </summary>
internal sealed partial class CurrentTaskPage : ListPage
{
    private readonly TaskImporterHost _host;

    private IListItem[] _items = [];
    private bool _loaded;

    public CurrentTaskPage(TaskImporterHost host)
    {
        _host = host;

        Icon = Icons.Timer;
        Title = "Current task";
        Name = "Open";
        PlaceholderText = "Search today's tasks";
        ShowDetails = true;
    }

    public override IListItem[] GetItems()
    {
        // GetItems is called often; fetch once and then only when an action has
        // invalidated what is on screen.
        if (!_loaded)
        {
            _loaded = true;
            _items = Load();
        }

        return _items;
    }

    private void Refresh()
    {
        _items = Load();
        RaiseItemsChanged(_items.Length);
    }

    private IListItem[] Load()
    {
        if (!_host.Settings.IsConfigured)
        {
            return [Message(
                "No access token yet",
                "Add one in the extension's settings. Super Productivity: Settings → Misc → Local REST API.")];
        }

        IsLoading = true;

        try
        {
            var current = Sync.Run(_host.GetCurrentTaskAsync);
            var today = Sync.Run(_host.GetTodayAsync);

            var items = new List<IListItem>();

            if (current is not null)
            {
                items.Add(new ListItem(new StopTimerCommand(_host, Refresh))
                {
                    Title = current.Title,
                    Subtitle = "Running · " + Spent(current),
                    Icon = Icons.Stop,
                    Tags = [new Tag("Running")],
                    MoreCommands =
                    [
                        new CommandContextItem(
                            new CompleteTaskCommand(_host, current.Id, current.Title, Refresh)),
                    ],
                    Details = DetailsFor(current),
                });
            }
            else
            {
                items.Add(Message("Nothing running", "Pick a task below to start its timer."));
            }

            foreach (var task in today)
            {
                if (task.IsDone) continue;
                if (current is not null && string.Equals(task.Id, current.Id, StringComparison.Ordinal)) continue;

                items.Add(new ListItem(new StartTaskCommand(_host, task.Id, task.Title, Refresh))
                {
                    Title = task.Title,
                    Subtitle = Spent(task),
                    Icon = Icons.Play,
                    MoreCommands =
                    [
                        new CommandContextItem(
                            new CompleteTaskCommand(_host, task.Id, task.Title, Refresh)),
                    ],
                    Details = DetailsFor(task),
                });
            }

            return [.. items];
        }
        catch (Exception exception)
        {
            return [Message("Could not reach Super Productivity", Sync.Explain(exception))];
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static Details DetailsFor(TaskDto task) => new()
    {
        Title = task.Title,
        Body = string.IsNullOrWhiteSpace(task.Notes) ? string.Empty : task.Notes,
        Metadata = task.DueDay is null
            ? []
            : [new DetailsElement { Key = "Due", Data = new DetailsLink(task.DueDay) }],
    };

    /// <summary>
    /// Time spent against the estimate, when there is one. Both arrive as
    /// milliseconds.
    /// </summary>
    private static string Spent(TaskDto task)
    {
        var spent = TimeProgress.Humanise(task.TimeSpent);
        return task.TimeEstimate > 0 ? $"{spent} of {TimeProgress.Humanise(task.TimeEstimate)}" : spent;
    }

    private static ListItem Message(string title, string subtitle) => new(new NoOpCommand())
    {
        Title = title,
        Subtitle = subtitle,
        Icon = Icons.Hint,
    };
}
