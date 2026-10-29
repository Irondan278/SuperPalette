using System;

namespace TaskImporter.SuperProductivity;

/// <summary>
/// Connection settings. The base address is configurable even though the port
/// is currently fixed at 3876, because the API docs say "not configurable in
/// v1" — which implies it will be one day.
/// </summary>
public sealed class SuperProductivityOptions
{
    public const string DefaultBaseUrl = "http://127.0.0.1:3876";

    public string BaseUrl { get; set; } = DefaultBaseUrl;

    /// <summary>From Settings → Misc → Access Token in Super Productivity.</summary>
    public string AccessToken { get; set; } = string.Empty;

    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>How long a projects/tags snapshot is reused before refetching.</summary>
    public TimeSpan ReferenceCacheTtl { get; set; } = TimeSpan.FromSeconds(60);

    public bool HasToken => !string.IsNullOrWhiteSpace(AccessToken);

    public Uri BuildUri(string relativePath)
    {
        var baseUri = new Uri(BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        return new Uri(baseUri, relativePath.TrimStart('/'));
    }
}
