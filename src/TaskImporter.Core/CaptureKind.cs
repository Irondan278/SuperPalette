namespace TaskImporter.Core;

/// <summary>
/// What the captured text appears to be. Routing rules match on this, so the
/// set is deliberately coarse: it describes shape, not meaning.
/// </summary>
public enum CaptureKind
{
    /// <summary>Ordinary text with no recognised structure.</summary>
    PlainText,

    /// <summary>An http or https URL.</summary>
    Url,

    /// <summary>A Windows path or UNC share.</summary>
    FilePath,

    /// <summary>An email address on its own.</summary>
    Email,
}
