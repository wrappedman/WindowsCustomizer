using System.Text.Json.Serialization;

namespace WindowsCustomizer.Core.Models;

/// <summary>
/// Root document of a ".wcp" project file. Everything the user configures is serialized here so a
/// project can be closed and resumed later. The workspace folder holds all working artifacts.
/// </summary>
public sealed class ProjectModel
{
    /// <summary>Project file schema version (forward compatibility).</summary>
    public string SchemaVersion { get; set; } = "1.0";

    public string Name { get; set; } = string.Empty;

    /// <summary>Absolute path of the project workspace directory.</summary>
    public string WorkspacePath { get; set; } = string.Empty;

    public SourceIsoInfo Source { get; set; } = new();

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset ModifiedUtc { get; set; }

    public ProjectSettings Settings { get; set; } = new();

    /// <summary>All customization choices (users, drivers, updates, apps, registry, services, ...).</summary>
    public CustomizationState Customization { get; set; } = new();

    /// <summary>Free-form bag so newer app versions can keep unknown data written by plugins/preview builds.</summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>Full path of the .wcp file inside the workspace.</summary>
    [JsonIgnore]
    public string ProjectFilePath =>
        string.IsNullOrWhiteSpace(WorkspacePath) || string.IsNullOrWhiteSpace(Name)
            ? string.Empty
            : Path.Combine(WorkspacePath, Name + ".wcp");
}

/// <summary>Metadata about the *original* ISO. The ISO itself is never modified or copied.</summary>
public sealed class SourceIsoInfo
{
    /// <summary>Absolute path to the read-only source ISO chosen by the user.</summary>
    public string OriginalIsoPath { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    /// <summary>ISO volume label (used for the output ISO label).</summary>
    public string VolumeLabel { get; set; } = string.Empty;

    /// <summary>Install payload info captured during analysis (shown on the Edition page).</summary>
    public string InstallImageFormat { get; set; } = string.Empty;

    public int ImageCount { get; set; }

    public string ImageCompression { get; set; } = string.Empty;

    /// <summary>Lower-case hex SHA-256 of the ISO (integrity reference).</summary>
    public string Sha256 { get; set; } = string.Empty;

    public DateTimeOffset HashedUtc { get; set; }

    public DateTimeOffset AnalyzedUtc { get; set; }
}

public sealed class ProjectSettings
{
    public string Theme { get; set; } = "Dark";

    /// <summary>File name of the final ISO produced by the build engine.</summary>
    public string OutputFileName { get; set; } = "Windows-Custom.iso";

    /// <summary>Basic | Advanced — gates dangerous UI (spec: Advanced Mode).</summary>
    public string Mode { get; set; } = "Basic";

    /// <summary>When true, a project workspace ISO snapshot cache may be created.</summary>
    public bool CreateBackupsBeforeApply { get; set; } = true;
}

/// <summary>Workspace folder names — single source of truth for the layout defined in the spec.</summary>
public static class WorkspaceLayout
{
    public static readonly string[] RequiredFolders =
    {
        "Source", "Mount", "Images", "Drivers", "Updates",
        "Packages", "Registry", "Scripts", "Config", "Logs", "Output", "Temp"
    };

    public const string ProjectFileSearchPattern = "*.wcp";

    public const string SourceCacheFolder = "Source";
    public const string LogsFolder = "Logs";
}
