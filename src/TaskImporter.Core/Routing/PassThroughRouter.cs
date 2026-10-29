using System.Threading;
using System.Threading.Tasks;

namespace TaskImporter.Core.Routing;

/// <summary>
/// The v1 router: returns the draft exactly as it arrived.
/// </summary>
public sealed class PassThroughRouter : ITaskRouter
{
    public static readonly PassThroughRouter Instance = new();

    public ValueTask<TaskDraft> RouteAsync(
        CaptureContext context,
        TaskDraft draft,
        CancellationToken cancellationToken = default)
        => new(draft);
}
