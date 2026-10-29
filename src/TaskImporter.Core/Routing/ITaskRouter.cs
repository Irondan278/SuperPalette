using System.Threading;
using System.Threading.Tasks;

namespace TaskImporter.Core.Routing;

/// <summary>
/// The seam where automatic routing will live.
///
/// A router receives the capture and the draft built from whatever the user
/// typed, and returns the draft it wants submitted. The contract is that
/// anything the user set explicitly — check
/// <see cref="TaskDraft.WasSetExplicitly"/> — is left alone unless the rule
/// says otherwise. Rules fill gaps; they do not argue with the typist.
///
/// v1 ships only <see cref="PassThroughRouter"/>. The submit path calls this
/// unconditionally so that adding a real router changes nothing else.
/// </summary>
public interface ITaskRouter
{
    ValueTask<TaskDraft> RouteAsync(
        CaptureContext context,
        TaskDraft draft,
        CancellationToken cancellationToken = default);
}
