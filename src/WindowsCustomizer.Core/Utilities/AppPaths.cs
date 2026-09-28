namespace WindowsCustomizer.Core.Utilities;

/// <summary>
/// Centralized application data paths. Set the WINCUSTOMIZER_HOME environment variable to redirect
/// (used by tests and for portable installs). Layout:
///   {root}/settings.json, {root}/recent.json, {root}/logs/, {root}/presets/, {root}/templates/
/// </summary>
public sealed class AppPaths
{
    public const string HomeEnvironmentVariable = "WINCUSTOMIZER_HOME";

    public AppPaths(string? rootOverride = null)
    {
        Root = rootOverride ?? DetectRoot();
    }

    public string Root { get; }

    public string SettingsFile => Path.Combine(Root, "settings.json");

    public string RecentProjectsFile => Path.Combine(Root, "recent.json");

    public string LogsDirectory => Path.Combine(Root, "logs");

    public string PresetsDirectory => Path.Combine(Root, "presets");

    public string TemplatesDirectory => Path.Combine(Root, "templates");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(PresetsDirectory);
        Directory.CreateDirectory(TemplatesDirectory);
    }

    private static string DetectRoot()
    {
        var fromEnv = Environment.GetEnvironmentVariable(HomeEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return Path.GetFullPath(fromEnv);
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(string.IsNullOrWhiteSpace(appData) ? "." : appData, "WindowsCustomizer");
    }
}
