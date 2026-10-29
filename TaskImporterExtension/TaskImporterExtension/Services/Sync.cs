using System;
using System.Threading;
using System.Threading.Tasks;
using TaskImporter.SuperProductivity;

namespace TaskImporterExtension.Services;

/// <summary>
/// Bridges Command Palette's synchronous <c>Invoke</c> onto the async REST
/// client, and turns a failure into a sentence rather than a stack trace.
/// </summary>
internal static class Sync
{
    /// <summary>How long any single user-initiated call gets before it is cut off.</summary>
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Runs async work to completion on the thread pool.
    ///
    /// <c>Task.Run</c> rather than a bare <c>GetAwaiter().GetResult()</c> so the
    /// continuation can never come back to a caller that is blocked waiting for
    /// it. The COM server is MTA and has no synchronisation context, but that is
    /// an invariant worth not depending on.
    /// </summary>
    public static T Run<T>(Func<CancellationToken, Task<T>> work)
    {
        using var cts = new CancellationTokenSource(Budget);
        return Task.Run(() => work(cts.Token), cts.Token).GetAwaiter().GetResult();
    }

    public static void Run(Func<CancellationToken, Task> work)
    {
        using var cts = new CancellationTokenSource(Budget);
        Task.Run(() => work(cts.Token), cts.Token).GetAwaiter().GetResult();
    }

    /// <summary>
    /// The message to show the user for a failed call. Super Productivity's own
    /// faults already explain themselves; everything else gets a plain summary.
    /// </summary>
    public static string Explain(Exception exception)
    {
        return exception switch
        {
            SuperProductivityException sp => sp.FriendlyMessage,
            OperationCanceledException or TimeoutException =>
                "Super Productivity did not answer in time.",
            AggregateException aggregate when aggregate.InnerException is not null =>
                Explain(aggregate.InnerException),
            _ => exception.Message,
        };
    }
}
