using System;
using TaskImporter.Core.Parsing;

namespace TaskImporter.Core.Routing;

/// <summary>
/// Turns the settings-level "default project" — which the user types as a name,
/// but might paste as an id — into an id the API will accept.
///
/// Lives in Core rather than in the extension so it is covered by the fast test
/// loop: the failure it guards against, sending a projectId that does not
/// exist, is rejected by the API and would otherwise only show up as a failed
/// capture.
/// </summary>
public static class DefaultProjectResolver
{
    /// <summary>
    /// The id to use, or null to let Super Productivity apply its own default.
    ///
    /// Never invents an id. An unresolvable setting yields null, because a task
    /// landing in the Inbox is a much better failure than a task that is
    /// refused outright.
    /// </summary>
    public static string? Resolve(ReferenceSnapshot snapshot, string? configured)
    {
        if (snapshot is null) return null;
        if (string.IsNullOrWhiteSpace(configured)) return null;

        var wanted = configured.Trim();

        // A name is what the user actually types, so it wins.
        var byName = snapshot.FindProjectByName(wanted);
        if (byName is not null) return byName.Value.Id;

        // An id pasted straight in is still accepted, but only one that the
        // server has confirmed exists.
        foreach (var project in snapshot.Projects)
        {
            if (string.Equals(project.Id, wanted, StringComparison.Ordinal)) return project.Id;
        }

        return null;
    }
}
