using System;
using System.IO;
using Microsoft.CommandPalette.Extensions.Toolkit;
using TaskImporter.SuperProductivity;

namespace TaskImporterExtension;

/// <summary>
/// The four things the extension needs to know, persisted next to the user's
/// other local app data rather than in the package, so reinstalling the
/// extension does not lose the access token.
///
/// Settings are read on every submit rather than cached, because the cost is a
/// dictionary lookup and the alternative is a stale token after the user pastes
/// a new one.
/// </summary>
internal sealed class TaskImporterSettings : JsonSettingsManager
{
    private const string Prefix = "taskimporter.";

    private const string KeyAccessToken = Prefix + "accessToken";
    private const string KeyBaseUrl = Prefix + "baseUrl";
    private const string KeyDefaultProject = Prefix + "defaultProject";
    private const string KeyFetchPageTitles = Prefix + "fetchPageTitles";

    private readonly TextSetting _accessToken = new(
        KeyAccessToken,
        "Access token",
        "From Super Productivity: Settings → Misc → Local REST API. The API is off by default, and the token only appears once it is enabled.",
        string.Empty);

    private readonly TextSetting _baseUrl = new(
        KeyBaseUrl,
        "API address",
        "Where Super Productivity is listening. The port is not configurable in the current version, so leave this alone unless you know otherwise.",
        SuperProductivityOptions.DefaultBaseUrl);

    private readonly TextSetting _defaultProject = new(
        KeyDefaultProject,
        "Default project",
        "Where a task goes when the line has no +project. Leave blank to let Super Productivity decide, which means the Inbox.",
        "Inbox");

    private readonly ToggleSetting _fetchPageTitles = new(
        KeyFetchPageTitles,
        "Look up page titles",
        "When a captured link has no title of its own, fetch the page and read it. A blocked host just falls back to a tidied URL.",
        true);

    public TaskImporterSettings()
    {
        FilePath = SettingsPath();

        Settings.Add(_accessToken);
        Settings.Add(_baseUrl);
        Settings.Add(_defaultProject);
        Settings.Add(_fetchPageTitles);

        LoadSettings();

        Settings.SettingsChanged += (_, _) =>
        {
            SaveSettings();
            Changed?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>Raised after a save, so the host can drop a stale REST client.</summary>
    public event EventHandler? Changed;

    public string AccessToken => Settings.GetSetting<string>(KeyAccessToken)?.Trim() ?? string.Empty;

    public string BaseUrl
    {
        get
        {
            var value = Settings.GetSetting<string>(KeyBaseUrl)?.Trim();
            return string.IsNullOrEmpty(value) ? SuperProductivityOptions.DefaultBaseUrl : value;
        }
    }

    /// <summary>A project name, or an id pasted verbatim. Resolved by the host.</summary>
    public string DefaultProject => Settings.GetSetting<string>(KeyDefaultProject)?.Trim() ?? string.Empty;

    public bool FetchPageTitles => Settings.GetSetting<bool>(KeyFetchPageTitles);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(AccessToken);

    /// <summary>
    /// Kept out of the package install folder on purpose: an MSIX upgrade
    /// replaces that directory, and losing the token on every rebuild would
    /// make the develop loop miserable.
    /// </summary>
    private static string SettingsPath()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TaskImporter");

        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "settings.json");
    }
}
