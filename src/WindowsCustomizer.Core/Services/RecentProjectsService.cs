using System.Text.Json;
using WindowsCustomizer.Core.Logging;
using WindowsCustomizer.Core.Models;
using WindowsCustomizer.Core.Utilities;

namespace WindowsCustomizer.Core.Services;

/// <summary>One entry of the start-page "Recent Projects" list.</summary>
public sealed class RecentProjectItem
{
    public string Name { get; set; } = string.Empty;

    public string ProjectFile { get; set; } = string.Empty;

    public string SourceIsoName { get; set; } = string.Empty;

    public DateTimeOffset LastOpenedUtc { get; set; }
}

/// <summary>Persistent recent-projects list (JSON in the app data root).</summary>
public sealed class RecentProjectsService
{
    private const int MaxRecent = 12;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly AppPaths _paths;
    private readonly IAppLogger _log;

    public RecentProjectsService(AppPaths paths, IAppLogger log)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>Loads recents, silently dropping entries whose project file no longer exists.</summary>
    public IReadOnlyList<RecentProjectItem> Load()
    {
        try
        {
            if (!File.Exists(_paths.RecentProjectsFile))
            {
                return Array.Empty<RecentProjectItem>();
            }

            var json = File.ReadAllText(_paths.RecentProjectsFile);
            var items = JsonSerializer.Deserialize<List<RecentProjectItem>>(json, JsonOptions) ?? new List<RecentProjectItem>();
            return items
                .Where(i => !string.IsNullOrWhiteSpace(i.ProjectFile) && File.Exists(i.ProjectFile))
                .OrderByDescending(i => i.LastOpenedUtc)
                .Take(MaxRecent)
                .ToList();
        }
        catch (Exception ex)
        {
            _log.Warn("RecentProjects", $"Failed to load recent projects: {ex.Message}");
            return Array.Empty<RecentProjectItem>();
        }
    }

    public void Add(ProjectModel project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var items = Load().ToList();
        items.RemoveAll(i => string.Equals(i.ProjectFile, project.ProjectFilePath, StringComparison.OrdinalIgnoreCase));
        items.Insert(0, new RecentProjectItem
        {
            Name = project.Name,
            ProjectFile = project.ProjectFilePath,
            SourceIsoName = project.Source.FileName,
            LastOpenedUtc = DateTimeOffset.UtcNow
        });

        Save(items.Take(MaxRecent).ToList());
    }

    public void Remove(string projectFile)
    {
        var items = Load().ToList();
        items.RemoveAll(i => string.Equals(i.ProjectFile, projectFile, StringComparison.OrdinalIgnoreCase));
        Save(items);
    }

    public void Clear()
    {
        Save(new List<RecentProjectItem>());
        _log.Info("RecentProjects", "Recent projects list cleared.");
    }

    private void Save(List<RecentProjectItem> items)
    {
        try
        {
            _paths.EnsureCreated();
            var json = JsonSerializer.Serialize(items, JsonOptions);
            var temp = _paths.RecentProjectsFile + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _paths.RecentProjectsFile, overwrite: true);
        }
        catch (Exception ex)
        {
            _log.Warn("RecentProjects", $"Failed to persist recent projects: {ex.Message}");
        }
    }
}
