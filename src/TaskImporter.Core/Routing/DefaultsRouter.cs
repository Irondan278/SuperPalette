using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TaskImporter.Core.Routing;

/// <summary>
/// Applies the settings-level defaults — default project and default tags —
/// to anything the user did not set explicitly.
///
/// This is not the rule engine. It exists so that "the default project comes
/// from settings" is implemented once, in the same place a rule would act,
/// rather than being smeared across the submit commands.
/// </summary>
public sealed class DefaultsRouter : ITaskRouter
{
    private readonly Func<string?> _defaultProjectId;
    private readonly Func<IReadOnlyList<string>> _defaultTagIds;

    public DefaultsRouter(
        Func<string?> defaultProjectId,
        Func<IReadOnlyList<string>>? defaultTagIds = null)
    {
        _defaultProjectId = defaultProjectId;
        _defaultTagIds = defaultTagIds ?? (static () => Array.Empty<string>());
    }

    public ValueTask<TaskDraft> RouteAsync(
        CaptureContext context,
        TaskDraft draft,
        CancellationToken cancellationToken = default)
    {
        if (!draft.WasSetExplicitly(nameof(TaskDraft.ProjectId)) && draft.ProjectId is null)
        {
            var fallback = _defaultProjectId();
            if (!string.IsNullOrWhiteSpace(fallback)) draft.ProjectId = fallback;
        }

        if (!draft.WasSetExplicitly(nameof(TaskDraft.TagIds)))
        {
            foreach (var tagId in _defaultTagIds()) draft.AddTag(tagId);
        }

        return new ValueTask<TaskDraft>(draft);
    }
}
