using System.Text.Json;
using System.Windows;
using WindowsCustomizer.Core.Utilities;

namespace WindowsCustomizer.App.Infrastructure;

/// <summary>Persisted application settings (per user, outside any project). Light theme is fixed.</summary>
public sealed class AppSettings
{
    public string DefaultWorkspaceRoot { get; set; } = string.Empty;

    public string Mode { get; set; } = "Basic";

    public bool ShowAdvancedToolsWarning { get; set; } = true;
}

/// <summary>Loads/saves <see cref="AppSettings"/> as JSON inside the app data root.</summary>
public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly AppPaths _paths;

    public AppSettingsStore(AppPaths paths)
    {
        _paths = paths;
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_paths.SettingsFile))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(
                    File.ReadAllText(_paths.SettingsFile), JsonOptions);
                if (settings is not null)
                {
                    return settings;
                }
            }
        }
        catch
        {
            // Corrupt settings must never block startup — fall through to defaults.
        }

        var fresh = new AppSettings
        {
            DefaultWorkspaceRoot = DefaultWorkspaceRoot()
        };
        Save(fresh);
        return fresh;
    }

    public void Save(AppSettings settings)
    {
        try
        {
            _paths.EnsureCreated();
            var temp = _paths.SettingsFile + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temp, _paths.SettingsFile, overwrite: true);
        }
        catch
        {
            // Settings persistence is best-effort.
        }
    }

    public static string DefaultWorkspaceRoot()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(string.IsNullOrWhiteSpace(docs) ? "." : docs, "WindowsCustomizer Projects");
    }
}

/// <summary>Detects administrator rights (spec: Administrator Requirement).</summary>
public static class PrivilegeService
{
    public static bool IsAdministrator
    {
        get
        {
            try
            {
                if (!OperatingSystem.IsWindows())
                {
                    return false;
                }

                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }
}
