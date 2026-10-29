using System;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using TaskImporterExtension.Services;

namespace TaskImporterExtension.Commands;

/// <summary>
/// Sends one quick-add line to Super Productivity.
///
/// The page rebuilds this on every keystroke, so the constructor stays free of
/// work — the line is captured, enriched and posted only when it is invoked.
/// </summary>
internal sealed partial class SubmitTaskCommand : InvokableCommand
{
    private readonly TaskImporterHost _host;
    private readonly string _line;
    private readonly string? _anchorText;
    private readonly Action _onSubmitted;

    public SubmitTaskCommand(TaskImporterHost host, string line, string? anchorText, Action onSubmitted)
    {
        _host = host;
        _line = line;
        _anchorText = anchorText;
        _onSubmitted = onSubmitted;

        Name = "Add task";
        Icon = Icons.Add;
    }

    public override ICommandResult Invoke()
    {
        if (string.IsNullOrWhiteSpace(_line)) return Keep("Type something to add first.");

        if (!_host.Settings.IsConfigured)
        {
            return Keep("No access token yet. Add one in the extension's settings, from Super Productivity's Settings → Misc.");
        }

        try
        {
            var created = Sync.Run(token => _host.SubmitAsync(_line, _anchorText, token));

            // The line has been sent, so it should not still be sitting there
            // waiting to be sent again the next time this page is opened.
            _onSubmitted();

            return CommandResult.ShowToast($"Added “{created.Title}”");
        }
        catch (Exception exception)
        {
            // Keeping the palette open leaves the typed line on screen, so a
            // failed submit is retryable rather than lost.
            return Keep(Sync.Explain(exception));
        }
    }

    /// <summary>A toast that leaves the page, and the typed line, where it was.</summary>
    private static CommandResult Keep(string message)
        => CommandResult.ShowToast(new ToastArgs
        {
            Message = message,
            Result = CommandResult.KeepOpen(),
        });
}
