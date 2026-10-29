using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TaskImporter.Core.Enrichment;

/// <summary>
/// Resolves a captured URL into a page title by fetching it.
///
/// Reads at most <see cref="MaxBytes"/> of the response, because the title is
/// in the head and downloading a whole page to find it would be rude. Any
/// failure — timeout, non-HTML content, a blocked host — leaves the context
/// untouched so the caller falls back to anchor text or a tidied URL.
/// </summary>
public sealed class PageTitleEnricher : IEnricher
{
    /// <summary>64 KB is comfortably past the head of any sane page.</summary>
    public const int MaxBytes = 64 * 1024;

    private readonly HttpClient _http;
    private readonly TimeSpan _timeout;

    public PageTitleEnricher(HttpClient http, TimeSpan? timeout = null)
    {
        _http = http;
        _timeout = timeout ?? TimeSpan.FromSeconds(3);
    }

    public bool CanEnrich(CaptureContext context)
        => context.Url is not null &&
           string.IsNullOrWhiteSpace(context.PageTitle) &&
           (context.Url.Scheme == Uri.UriSchemeHttp || context.Url.Scheme == Uri.UriSchemeHttps);

    public async Task EnrichAsync(CaptureContext context, CancellationToken cancellationToken = default)
    {
        if (!CanEnrich(context)) return;

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_timeout);

        try
        {
            var html = await ReadHeadAsync(context.Url!, timeoutSource.Token).ConfigureAwait(false);
            if (html is null) return;

            var title = PageTitleExtractor.Extract(html);
            if (title is null) return;

            context.PageTitle = PageTitleExtractor.TrimSiteSuffix(title, context.Url!.Host);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or
                                       InvalidOperationException or UriFormatException)
        {
            // Enrichment is best-effort by contract. The caller has a fallback.
        }
    }

    private async Task<string?> ReadHeadAsync(Uri url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return null;

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is not null &&
            !mediaType.Contains("html", StringComparison.OrdinalIgnoreCase) &&
            !mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        var buffer = new byte[MaxBytes];
        var total = 0;

        while (total < buffer.Length)
        {
            var read = await stream
                .ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken)
                .ConfigureAwait(false);

            if (read == 0) break;
            total += read;
        }

        if (total == 0) return null;

        // Almost everything is UTF-8; a wrong guess only costs a mangled
        // character in a title, never an exception.
        return Encoding.UTF8.GetString(buffer, 0, total);
    }

    /// <summary>
    /// An HttpClient configured the way a page-title fetch wants: redirects
    /// followed, compression on, and a browser-ish user agent, because a fair
    /// number of sites serve nothing useful without one.
    /// </summary>
    public static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            AutomaticDecompression = System.Net.DecompressionMethods.All,
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(10), // Per-call timeouts do the real work.
        };

        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
            "Chrome/131.0.0.0 Safari/537.36 TaskImporter/1.0");

        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "Accept", "text/html,application/xhtml+xml");

        return client;
    }
}
