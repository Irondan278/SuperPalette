using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TaskImporter.Core.Parsing;

namespace TaskImporter.SuperProductivity;

/// <summary>
/// Keeps a short-lived snapshot of projects and tags so the parser can resolve
/// names on every keystroke without touching the network.
///
/// The extension process is long-lived, so this lives for the whole session and
/// refreshes on a TTL. Reads never block on a refresh: a stale snapshot is
/// returned while the new one is fetched, because a one-minute-old project list
/// is always better than a stalled palette.
/// </summary>
public sealed class ReferenceCache
{
    private readonly SuperProductivityClient _client;
    private readonly TimeSpan _ttl;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private ReferenceSnapshot _snapshot = ReferenceSnapshot.Empty;
    private DateTimeOffset _fetchedAt = DateTimeOffset.MinValue;
    private bool _refreshing;

    public ReferenceCache(SuperProductivityClient client, TimeSpan? ttl = null)
    {
        _client = client;
        _ttl = ttl ?? client.Options.ReferenceCacheTtl;
    }

    /// <summary>The snapshot as it stands. Never blocks, never throws.</summary>
    public ReferenceSnapshot Current => _snapshot;

    public bool IsStale => DateTimeOffset.UtcNow - _fetchedAt > _ttl;

    /// <summary>
    /// Returns the current snapshot, kicking off a background refresh when it
    /// has gone stale. Call this from the parse path.
    /// </summary>
    public ReferenceSnapshot GetAndRefreshIfStale()
    {
        if (IsStale && !_refreshing) _ = Task.Run(() => RefreshAsync());
        return _snapshot;
    }

    /// <summary>
    /// Fetches projects and tags. Failures leave the previous snapshot in place,
    /// so losing the connection degrades autocomplete rather than breaking entry.
    /// </summary>
    public async Task<ReferenceSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!await _refreshLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return _snapshot;
        }

        _refreshing = true;
        try
        {
            var projectsTask = _client.GetProjectsAsync(cancellationToken);
            var tagsTask = _client.GetTagsAsync(cancellationToken);
            await Task.WhenAll(projectsTask, tagsTask).ConfigureAwait(false);

            var projects = new List<NamedItem>();
            foreach (var project in await projectsTask.ConfigureAwait(false))
            {
                projects.Add(new NamedItem(project.Id, project.DisplayName));
            }

            var tags = new List<NamedItem>();
            foreach (var tag in await tagsTask.ConfigureAwait(false))
            {
                tags.Add(new NamedItem(tag.Id, tag.DisplayName));
            }

            _snapshot = new ReferenceSnapshot(projects, tags);
            _fetchedAt = DateTimeOffset.UtcNow;
            return _snapshot;
        }
        catch (Exception ex) when (ex is SuperProductivityException or OperationCanceledException)
        {
            return _snapshot;
        }
        finally
        {
            _refreshing = false;
            _refreshLock.Release();
        }
    }

    /// <summary>Forces the next read to refetch.</summary>
    public void Invalidate() => _fetchedAt = DateTimeOffset.MinValue;
}
