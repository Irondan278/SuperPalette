using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TaskImporter.Core;
using TaskImporter.Core.Enrichment;
using TaskImporter.Core.Parsing;
using TaskImporter.Core.Routing;
using TaskImporter.SuperProductivity;
using TaskImporter.SuperProductivity.Models;

namespace TaskImporterExtension.Services;

/// <summary>
/// The single place the pages get a parser, a REST client and a submit path.
///
/// One instance lives for the whole extension process, which is what makes the
/// reference cache worth having: projects and tags are fetched once a minute at
/// most, and never on the keystroke path.
/// </summary>
internal sealed partial class TaskImporterHost : IDisposable
{
    private readonly TaskImporterSettings _settings;
    private readonly HttpClient _titleHttp;
    private readonly Lock _gate = new();

    private SuperProductivityClient? _client;
    private ReferenceCache? _cache;
    private string _connectionKey = string.Empty;
    private bool _disposed;

    public TaskImporterHost(TaskImporterSettings settings)
    {
        _settings = settings;
        _titleHttp = PageTitleEnricher.CreateHttpClient();

        // A new token or address means the next call must not reuse the old
        // client, and the cached projects belong to the old instance.
        _settings.Changed += (_, _) => Reset();
    }

    public TaskImporterSettings Settings => _settings;

    // ---- Parsing --------------------------------------------------------

    /// <summary>
    /// Parses a quick-add line. Called on every keystroke, so it must never
    /// touch the network: the parser resolves names against whatever snapshot
    /// the cache already holds, and a stale refresh happens in the background.
    /// </summary>
    public CaptureContext Capture(string line, string? anchorText = null)
    {
        var cache = Connection().Cache;
        var parser = new QuickAddParser(cache.GetAndRefreshIfStale());
        return DraftFactory.Capture(line, parser, anchorText);
    }

    // ---- Submitting -----------------------------------------------------

    /// <summary>
    /// Runs the full capture → enrich → route → POST path and returns the task
    /// Super Productivity created.
    /// </summary>
    public async Task<TaskDto> SubmitAsync(
        string line,
        string? anchorText,
        CancellationToken cancellationToken = default)
    {
        var (client, cache) = Connection();
        var parser = new QuickAddParser(cache.GetAndRefreshIfStale());
        var context = DraftFactory.Capture(line, parser, anchorText);

        await EnrichAsync(context, cancellationToken).ConfigureAwait(false);

        var draft = DraftFactory.Build(context);

        var router = new RouterPipeline(
            new DefaultsRouter(() => DefaultProjectResolver.Resolve(cache.Current, _settings.DefaultProject)));

        draft = await router.RouteAsync(context, draft, cancellationToken).ConfigureAwait(false);

        var created = await client.CreateTaskAsync(draft, cancellationToken).ConfigureAwait(false);

        // A brand new project or tag may have been referenced; let the next
        // parse see it rather than waiting out the TTL.
        if (draft.ProjectId is not null) cache.Invalidate();

        return created;
    }

    /// <summary>
    /// Fills in a page title for a captured link. A failure here is not a
    /// failure of the capture — a corporate firewall may block some hosts, and the
    /// tidied URL is a perfectly good title.
    /// </summary>
    private async Task EnrichAsync(CaptureContext context, CancellationToken cancellationToken)
    {
        if (!_settings.FetchPageTitles || context.Url is null) return;

        var enricher = new PageTitleEnricher(_titleHttp);
        if (!enricher.CanEnrich(context)) return;

        try
        {
            await enricher.EnrichAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Deliberately swallowed: see above.
        }
    }

    // ---- The timer ------------------------------------------------------

    public Task<TaskDto?> GetCurrentTaskAsync(CancellationToken cancellationToken = default)
        => Connection().Client.GetCurrentTaskAsync(cancellationToken);

    public Task<IReadOnlyList<TaskDto>> GetTodayAsync(CancellationToken cancellationToken = default)
        => Connection().Client.GetTodayAsync(cancellationToken);

    public Task StartTaskAsync(string taskId, CancellationToken cancellationToken = default)
        => Connection().Client.StartTaskAsync(taskId, cancellationToken);

    public Task StopCurrentTaskAsync(CancellationToken cancellationToken = default)
        => Connection().Client.StopCurrentTaskAsync(cancellationToken);

    public Task<TaskDto> CompleteTaskAsync(string taskId, CancellationToken cancellationToken = default)
        => Connection().Client.CompleteTaskAsync(taskId, cancellationToken);

    // ---- Plumbing -------------------------------------------------------

    /// <summary>
    /// The client and cache for the current settings, rebuilt only when the
    /// address or token actually changes.
    /// </summary>
    private (SuperProductivityClient Client, ReferenceCache Cache) Connection()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var key = _settings.BaseUrl + "\u0000" + _settings.AccessToken;

            if (_client is null || _cache is null || !string.Equals(_connectionKey, key, StringComparison.Ordinal))
            {
                _client?.Dispose();

                var client = new SuperProductivityClient(new SuperProductivityOptions
                {
                    BaseUrl = _settings.BaseUrl,
                    AccessToken = _settings.AccessToken,
                });

                _client = client;
                _cache = new ReferenceCache(client);
                _connectionKey = key;
            }

            return (_client, _cache);
        }
    }

    private void Reset()
    {
        lock (_gate)
        {
            _client?.Dispose();
            _client = null;
            _cache = null;
            _connectionKey = string.Empty;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _client?.Dispose();
            _client = null;
            _cache = null;
        }

        _titleHttp.Dispose();
    }
}
