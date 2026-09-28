using System.Text.Json;
using WindowsCustomizer.Core.Logging;
using WindowsCustomizer.Core.Models;

namespace WindowsCustomizer.Core.Services;

// =================================================================================================
// Backup / Rollback (spec 30). Snapshots the project decision state (.wcp) before every major
// operation so the user can undo/rollback. Image-level backups are handled by the Build Engine
// (working-copy WIM is never the original; DISM /Discard leaves the original untouched).
// =================================================================================================

public sealed record ProjectSnapshot(string Id, DateTimeOffset TakenUtc, string Reason, string FilePath);

public sealed class ProjectSnapshotService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly IAppLogger _log;

    public ProjectSnapshotService(IAppLogger log)
    {
        _log = log;
    }

    private static string SnapshotDir(ProjectModel project) =>
        Path.Combine(project.WorkspacePath, "Config", "snapshots");

    public ProjectSnapshot TakeSnapshot(ProjectModel project, string reason)
    {
        var dir = SnapshotDir(project);
        Directory.CreateDirectory(dir);
        var id = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var file = Path.Combine(dir, $"{id}.wcp.snapshot");
        File.WriteAllText(file, JsonSerializer.Serialize(project, JsonOptions));
        _log.Info("Snapshot", $"Snapshot {id} taken: {reason}");
        return new ProjectSnapshot(id, DateTimeOffset.UtcNow, reason, file);
    }

    public IReadOnlyList<ProjectSnapshot> List(ProjectModel project)
    {
        var dir = SnapshotDir(project);
        if (!Directory.Exists(dir))
        {
            return Array.Empty<ProjectSnapshot>();
        }

        return Directory.GetFiles(dir, "*.wcp.snapshot")
            .OrderByDescending(f => f)
            .Select(f =>
            {
                var id = Path.GetFileNameWithoutExtension(f).Replace(".wcp", "");
                return new ProjectSnapshot(id, File.GetLastWriteTimeUtc(f), "", f);
            })
            .ToList();
    }

    public void Restore(ProjectModel project, string snapshotId)
    {
        var file = Path.Combine(SnapshotDir(project), snapshotId + ".wcp.snapshot");
        if (!File.Exists(file))
        {
            throw new FileNotFoundException($"Snapshot '{snapshotId}' not found.", file);
        }

        var restored = JsonSerializer.Deserialize<ProjectModel>(File.ReadAllText(file), JsonOptions)
            ?? throw new InvalidDataException("Snapshot file is corrupt.");

        // Copy state back into the live project object (workspace path stays the same).
        project.Customization = restored.Customization;
        project.Settings = restored.Settings;
        project.Source = restored.Source;

        new ProjectService(_log).SaveProject(project);
        _log.Success("Snapshot", $"Project rolled back to snapshot {snapshotId}.");
    }

    public void RollbackLast(ProjectModel project)
    {
        var snapshots = List(project);
        if (snapshots.Count == 0)
        {
            throw new InvalidOperationException("No snapshots available to roll back to.");
        }

        Restore(project, snapshots[0].Id);
    }

    /// <summary>Undo = roll back to the most recent snapshot and delete it (one-level undo).</summary>
    public void UndoLastChange(ProjectModel project)
    {
        var snapshots = List(project);
        if (snapshots.Count == 0)
        {
            throw new InvalidOperationException("Nothing to undo.");
        }

        var last = snapshots[0];
        Restore(project, last.Id);
        File.Delete(last.FilePath);
    }
}
