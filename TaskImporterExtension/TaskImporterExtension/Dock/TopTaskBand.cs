using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using TaskImporter.Core.Timing;
using TaskImporter.SuperProductivity.Models;
using TaskImporterExtension.Commands;
using TaskImporterExtension.Pages;
using TaskImporterExtension.Services;

namespace TaskImporterExtension.Dock;

/// <summary>
/// The dock band: the top task, its time against the estimate, and a button
/// that starts or stops its timer.
///
/// "Top task" is whatever the timer is on, or failing that the first open task
/// on today's list. Super Productivity is asked every <see cref="PollEvery"/>;
/// in between, a running timer is counted forward locally once a second, so
/// the band ticks without a request per tick.
/// </summary>
internal sealed partial class TopTaskBand : IDisposable
{
    private const string BandId = "TaskImporter.dock.topTask";

    private static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollBudget = TimeSpan.FromSeconds(10);

    /// <summary>The dock is narrow; a long title would push everything else off it.</summary>
    private const int MaxTitleLength = 36;

    private readonly TaskImporterHost _host;
    private readonly ListItem _status;
    private readonly ListItem _toggle;
    private readonly Timer _tick;

    private volatile Snapshot _snapshot = Snapshot.Waiting;
    /// <summary>When to ask next, in <see cref="Environment.TickCount64"/> milliseconds.</summary>
    private long _nextPollAt;
    private int _polling;
    private bool _disposed;

    public TopTaskBand(TaskImporterHost host, CurrentTaskPage currentTaskPage)
    {
        _host = host;

        _status = new ListItem(currentTaskPage)
        {
            Title = "Super Palette",
            Icon = Icons.Timer,
        };

        // An empty title is not an icon-only button: the Dock falls back to the
        // command's Name, which is far too wide. Render keeps this short label
        // in step with the timer.
        _toggle = new ListItem(new ToggleTimerCommand(this))
        {
            Title = "Start",
            Icon = Icons.Play,
        };

        Band = new WrappedDockItem([_status, _toggle], BandId, "Top task");

        // A new token or address should show up now, not at the next poll.
        _host.Settings.Changed += (_, _) => PollSoon();

        _tick = new Timer(_ => Tick(), null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
    }

    public ICommandItem Band { get; }

    /// <summary>Asks Super Productivity on the next tick rather than waiting out the interval.</summary>
    public void PollSoon() => Volatile.Write(ref _nextPollAt, 0);

    private void Tick()
    {
        var now = DateTimeOffset.Now;

        if (Environment.TickCount64 >= Volatile.Read(ref _nextPollAt) && Interlocked.CompareExchange(ref _polling, 1, 0) == 0)
        {
            Volatile.Write(ref _nextPollAt, Environment.TickCount64 + (long)PollEvery.TotalMilliseconds);
            _ = PollAsync();
        }

        Render(_snapshot, now);
    }

    private async Task PollAsync()
    {
        try
        {
            _snapshot = await FetchAsync().ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _polling, 0);
        }

        Render(_snapshot, DateTimeOffset.Now);
    }

    private async Task<Snapshot> FetchAsync()
    {
        if (!_host.Settings.IsConfigured)
        {
            return Snapshot.Problem("Add an access token in TaskImporter's settings");
        }

        using var cts = new CancellationTokenSource(PollBudget);

        try
        {
            var current = await _host.GetCurrentTaskAsync(cts.Token).ConfigureAwait(false);
            if (current is not null) return Snapshot.Of(current, running: true);

            var today = await _host.GetTodayAsync(cts.Token).ConfigureAwait(false);
            var top = today.FirstOrDefault(task => !task.IsDone);

            return top is null ? Snapshot.Empty : Snapshot.Of(top, running: false);
        }
        catch (Exception exception)
        {
            return Snapshot.Problem(Sync.Explain(exception));
        }
    }

    private void Render(Snapshot snapshot, DateTimeOffset now)
    {
        if (_disposed) return;

        string title;
        string subtitle;

        if (snapshot.Task is { } task)
        {
            var spent = TimeProgress.Extrapolate(task.TimeSpent, snapshot.Running, snapshot.FetchedAt, now);
            title = Shorten(task.Title);
            subtitle = TimeProgress.Describe(spent, task.TimeEstimate, snapshot.Running);
        }
        else
        {
            title = snapshot.Message;
            subtitle = string.Empty;
        }

        // Every set raises a property change across the process boundary, so
        // only touch what actually moved — once a second adds up.
        if (_status.Title != title) _status.Title = title;
        if (_status.Subtitle != subtitle) _status.Subtitle = subtitle;

        var icon = snapshot.Running ? Icons.Stop : Icons.Play;
        if (!ReferenceEquals(_toggle.Icon, icon)) _toggle.Icon = icon;

        var label = snapshot.Running ? "Stop" : "Start";
        if (_toggle.Title != label) _toggle.Title = label;
    }

    private static string Shorten(string title)
        => title.Length <= MaxTitleLength ? title : title[..(MaxTitleLength - 1)].TrimEnd() + "…";

    public void Dispose()
    {
        _disposed = true;
        _tick.Dispose();
    }

    /// <summary>What was true at the last poll.</summary>
    private sealed record Snapshot(TaskDto? Task, bool Running, DateTimeOffset FetchedAt, string Message)
    {
        public static Snapshot Waiting { get; } = new(null, false, default, "Super Palette");

        public static Snapshot Empty { get; } = new(null, false, default, "Nothing on today");

        public static Snapshot Of(TaskDto task, bool running) => new(task, running, DateTimeOffset.Now, string.Empty);

        public static Snapshot Problem(string message) => new(null, false, default, message);
    }

    /// <summary>
    /// Stops the timer if it is running, otherwise starts it on the top task.
    /// Decided at the moment of the click from the latest poll, so the button
    /// never needs swapping out for a different command.
    /// </summary>
    private sealed partial class ToggleTimerCommand : InvokableCommand
    {
        private readonly TopTaskBand _band;

        public ToggleTimerCommand(TopTaskBand band)
        {
            _band = band;

            Id = "TaskImporter.dock.toggleTimer";
            Name = "Start or stop the timer";
            Icon = Icons.Play;
        }

        public override ICommandResult Invoke()
        {
            var snapshot = _band._snapshot;
            var host = _band._host;

            if (snapshot.Task is not { } task)
            {
                return CommandResult.ShowToast(new ToastArgs
                {
                    Message = snapshot.Message,
                    Result = CommandResult.KeepOpen(),
                });
            }

            return snapshot.Running
                ? TimerResult.Run(
                    () => Sync.Run(host.StopCurrentTaskAsync),
                    "Timer stopped",
                    _band.PollSoon)
                : TimerResult.Run(
                    () => Sync.Run(token => host.StartTaskAsync(task.Id, token)),
                    $"Started “{task.Title}”",
                    _band.PollSoon);
        }
    }
}
