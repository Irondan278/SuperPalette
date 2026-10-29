using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TaskImporter.Core.Routing;

/// <summary>
/// Runs several routers in order, each seeing the draft the previous one
/// produced. The rule engine, when it arrives, is simply another entry.
/// </summary>
public sealed class RouterPipeline : ITaskRouter
{
    private readonly IReadOnlyList<ITaskRouter> _routers;

    public RouterPipeline(params ITaskRouter[] routers) => _routers = routers;

    public async ValueTask<TaskDraft> RouteAsync(
        CaptureContext context,
        TaskDraft draft,
        CancellationToken cancellationToken = default)
    {
        foreach (var router in _routers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            draft = await router.RouteAsync(context, draft, cancellationToken).ConfigureAwait(false);
        }

        return draft;
    }
}
