using System;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using TaskImporterExtension.Services;

namespace TaskImporterExtension.Commands;

/// <summary>Starts the timer on a task.</summary>
internal sealed partial class StartTaskCommand : InvokableCommand
{
    private readonly TaskImporterHost _host;
    private readonly string _taskId;
    private readonly string _title;
    private readonly Action _afterwards;

    public StartTaskCommand(TaskImporterHost host, string taskId, string title, Action afterwards)
    {
        _host = host;
        _taskId = taskId;
        _title = title;
        _afterwards = afterwards;

        Name = "Start";
        Icon = Icons.Play;
    }

    public override ICommandResult Invoke()
        => TimerResult.Run(
            () => Sync.Run(token => _host.StartTaskAsync(_taskId, token)),
            $"Started “{_title}”",
            _afterwards);
}

/// <summary>Stops whatever the timer is currently running.</summary>
internal sealed partial class StopTimerCommand : InvokableCommand
{
    private readonly TaskImporterHost _host;
    private readonly Action _afterwards;

    public StopTimerCommand(TaskImporterHost host, Action afterwards)
    {
        _host = host;
        _afterwards = afterwards;

        Name = "Stop timer";
        Icon = Icons.Stop;
    }

    public override ICommandResult Invoke()
        => TimerResult.Run(
            () => Sync.Run(_host.StopCurrentTaskAsync),
            "Timer stopped",
            _afterwards);
}

/// <summary>Marks a task done.</summary>
internal sealed partial class CompleteTaskCommand : InvokableCommand
{
    private readonly TaskImporterHost _host;
    private readonly string _taskId;
    private readonly string _title;
    private readonly Action _afterwards;

    public CompleteTaskCommand(TaskImporterHost host, string taskId, string title, Action afterwards)
    {
        _host = host;
        _taskId = taskId;
        _title = title;
        _afterwards = afterwards;

        Name = "Complete";
        Icon = Icons.Done;
    }

    public override ICommandResult Invoke()
        => TimerResult.Run(
            () => Sync.Run(token => _host.CompleteTaskAsync(_taskId, token)),
            $"Completed “{_title}”",
            _afterwards);
}

/// <summary>
/// The shared shape of every timer action: do the call, refresh the page behind
/// it, and stay put so the list can be worked through without reopening it.
/// </summary>
internal static class TimerResult
{
    public static ICommandResult Run(Action call, string success, Action afterwards)
    {
        string message;

        try
        {
            call();
            message = success;
        }
        catch (Exception exception)
        {
            message = Sync.Explain(exception);
        }

        // Refresh regardless: a failure often means the state moved underneath
        // us, and a stale list is the thing most likely to cause the next one.
        afterwards();

        return CommandResult.ShowToast(new ToastArgs
        {
            Message = message,
            Result = CommandResult.KeepOpen(),
        });
    }
}
