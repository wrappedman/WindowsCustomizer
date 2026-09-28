using System.Text.Json;
using WindowsCustomizer.Core.Logging;
using WindowsCustomizer.Core.Models;
using WindowsCustomizer.Core.Utilities;

namespace WindowsCustomizer.Core.Services;

/// <summary>Creates, opens and saves project workspaces (.wcp project files).</summary>
public interface IProjectService
{
    /// <summary>
    /// Creates a new workspace (folder tree + .wcp file). Never copies or touches the source ISO.
    /// </summary>
    ProjectModel CreateProject(string name, string workspaceParentDirectory, string sourceIsoPath);

    /// <summary>Opens a project from a .wcp file or from a workspace directory containing one.</summary>
    ProjectModel OpenProject(string projectFileOrWorkspace);

    void SaveProject(ProjectModel project);

    /// <summary>Lists missing/misplaced workspace folders (auto-heal candidates).</summary>
    IReadOnlyList<string> GetWorkspaceStructureIssues(string workspacePath);
}

public sealed class ProjectService : IProjectService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly IAppLogger _log;

    public ProjectService(IAppLogger log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public ProjectModel CreateProject(string name, string workspaceParentDirectory, string sourceIsoPath)
    {
        if (!PathGuard.IsValidProjectName(name, out var nameError))
        {
            throw new ArgumentException(nameError, nameof(name));
        }

        if (string.IsNullOrWhiteSpace(workspaceParentDirectory))
        {
            throw new ArgumentException("A workspace parent directory is required.", nameof(workspaceParentDirectory));
        }

        if (string.IsNullOrWhiteSpace(sourceIsoPath) || !File.Exists(sourceIsoPath))
        {
            throw new FileNotFoundException("The selected source ISO does not exist.", sourceIsoPath);
        }

        var trimmedName = name.Trim();
        var parent = Path.GetFullPath(workspaceParentDirectory);
        Directory.CreateDirectory(parent);

        var workspace = Path.Combine(parent, trimmedName);
        if (Directory.Exists(workspace) && Directory.EnumerateFileSystemEntries(workspace).Any())
        {
            throw new IOException($"The workspace folder '{workspace}' already exists and is not empty.");
        }

        Directory.CreateDirectory(workspace);
        _log.Info("Project", $"Creating project '{trimmedName}' in '{workspace}'.");

        foreach (var folder in WorkspaceLayout.RequiredFolders)
        {
            Directory.CreateDirectory(Path.Combine(workspace, folder));
        }

        // Keep a small pointer to the source ISO inside Source/ — the ISO itself is never copied.
        var sourceInfo = new SourceIsoInfo
        {
            OriginalIsoPath = Path.GetFullPath(sourceIsoPath),
            FileName = Path.GetFileName(sourceIsoPath),
            SizeBytes = new FileInfo(sourceIsoPath).Length
        };

        var project = new ProjectModel
        {
            Name = trimmedName,
            WorkspacePath = workspace,
            Source = sourceInfo,
            CreatedUtc = DateTimeOffset.UtcNow,
            ModifiedUtc = DateTimeOffset.UtcNow
        };

        SaveProject(project);
        _log.Success("Project", $"Project created: {project.ProjectFilePath}");
        return project;
    }

    public ProjectModel OpenProject(string projectFileOrWorkspace)
    {
        if (string.IsNullOrWhiteSpace(projectFileOrWorkspace))
        {
            throw new ArgumentException("A project file or workspace path is required.", nameof(projectFileOrWorkspace));
        }

        var projectFile = ResolveProjectFile(projectFileOrWorkspace);
        if (!File.Exists(projectFile))
        {
            throw new FileNotFoundException($"Project file not found: {projectFile}", projectFile);
        }

        var json = File.ReadAllText(projectFile);
        var project = JsonSerializer.Deserialize<ProjectModel>(json, JsonOptions)
            ?? throw new InvalidDataException($"The project file '{projectFile}' is empty or corrupt.");

        if (string.IsNullOrWhiteSpace(project.Name))
        {
            project.Name = Path.GetFileNameWithoutExtension(projectFile);
        }

        project.WorkspacePath = Path.GetDirectoryName(Path.GetFullPath(projectFile))!;

        // Heal missing folders (e.g. workspace was moved by hand) and report.
        foreach (var issue in GetWorkspaceStructureIssues(project.WorkspacePath))
        {
            _log.Warn("Project", $"Workspace issue: {issue}");
        }

        foreach (var folder in WorkspaceLayout.RequiredFolders)
        {
            Directory.CreateDirectory(Path.Combine(project.WorkspacePath, folder));
        }

        _log.Info("Project", $"Opened project '{project.Name}' ({project.ProjectFilePath}).");
        return project;
    }

    public void SaveProject(ProjectModel project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (string.IsNullOrWhiteSpace(project.WorkspacePath))
        {
            throw new InvalidOperationException("Project workspace path is not set.");
        }

        if (string.IsNullOrWhiteSpace(project.ProjectFilePath))
        {
            throw new InvalidOperationException("Project name is not set.");
        }

        Directory.CreateDirectory(project.WorkspacePath);
        project.ModifiedUtc = DateTimeOffset.UtcNow;

        var json = JsonSerializer.Serialize(project, JsonOptions);
        var target = project.ProjectFilePath;

        // Atomic-ish write: never leave a half-written .wcp behind.
        var temp = target + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, target, overwrite: true);
        _log.Debug("Project", $"Saved {target}");
    }

    public IReadOnlyList<string> GetWorkspaceStructureIssues(string workspacePath)
    {
        var issues = new List<string>();
        if (!Directory.Exists(workspacePath))
        {
            issues.Add($"Workspace does not exist: {workspacePath}");
            return issues;
        }

        foreach (var folder in WorkspaceLayout.RequiredFolders)
        {
            if (!Directory.Exists(Path.Combine(workspacePath, folder)))
            {
                issues.Add($"Missing folder '{folder}' (will be recreated).");
            }
        }

        var projectFiles = Directory.GetFiles(workspacePath, WorkspaceLayout.ProjectFileSearchPattern, SearchOption.TopDirectoryOnly);
        if (projectFiles.Length == 0)
        {
            issues.Add("No .wcp project file found in the workspace root.");
        }
        else if (projectFiles.Length > 1)
        {
            issues.Add($"Multiple .wcp files found; the first one is used: {string.Join(", ", projectFiles.Select(Path.GetFileName))}");
        }

        return issues;
    }

    private static string ResolveProjectFile(string path)
    {
        var full = Path.GetFullPath(path);

        if (File.Exists(full) && string.Equals(Path.GetExtension(full), ".wcp", StringComparison.OrdinalIgnoreCase))
        {
            return full;
        }

        if (Directory.Exists(full))
        {
            var files = Directory.GetFiles(full, WorkspaceLayout.ProjectFileSearchPattern, SearchOption.TopDirectoryOnly);
            return files.Length > 0
                ? files[0]
                : throw new FileNotFoundException($"No .wcp project file found in '{full}'.", full);
        }

        return full;
    }
}
