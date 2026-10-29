using System.Threading;
using System.Threading.Tasks;

namespace TaskImporter.Core.Enrichment;

/// <summary>
/// Something that adds information to a capture by doing I/O.
///
/// Enrichers run before routing and are always awaited with a timeout, because
/// the palette must stay responsive even when a page is slow or unreachable. An
/// enricher that fails does so silently: it leaves the context as it found it.
/// </summary>
public interface IEnricher
{
    /// <summary>Whether this enricher has anything to contribute.</summary>
    bool CanEnrich(CaptureContext context);

    Task EnrichAsync(CaptureContext context, CancellationToken cancellationToken = default);
}
