using WindowsCustomizer.Core.Logging;
using WindowsCustomizer.Core.Models;
using WindowsCustomizer.Core.Services;
using Xunit;

namespace WindowsCustomizer.Core.Tests;

public class ProjectServiceTests : IDisposable
{
    private readonly string _root;
    private readonly FileLogger _log;
    private readonly ProjectService _service;

    public ProjectServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "wc-proj-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _log = new FileLogger();
        _service = new ProjectService(_log);
    }

    private string MakeDummyIso()
    {
        var iso = Path.Combine(_root, "windows11.iso");
        File.WriteAllBytes(iso, new byte[2048]);
        return iso;
    }

    [Fact]
    public void CreateProject_CreatesWorkspaceLayoutAndProjectFile()
    {
        var iso = MakeDummyIso();
        var project = _service.CreateProject("MyWin11", _root, iso);

        Assert.Equal("MyWin11", project.Name);
        Assert.True(File.Exists(project.ProjectFilePath));
        foreach (var folder in WorkspaceLayout.RequiredFolders)
        {
            Assert.True(Directory.Exists(Path.Combine(project.WorkspacePath, folder)), $"missing {folder}");
        }

        Assert.Equal(Path.GetFullPath(iso), project.Source.OriginalIsoPath);
        Assert.Equal(2048, project.Source.SizeBytes);
    }

    [Fact]
    public void CreateProject_InvalidName_Throws()
    {
        var iso = MakeDummyIso();
        Assert.Throws<ArgumentException>(() => _service.CreateProject("bad/name", _root, iso));
        Assert.Throws<ArgumentException>(() => _service.CreateProject("", _root, iso));
    }

    [Fact]
    public void CreateProject_MissingIso_Throws()
    {
        Assert.Throws<FileNotFoundException>(() =>
            _service.CreateProject("Proj", _root, Path.Combine(_root, "nope.iso")));
    }

    [Fact]
    public void CreateProject_ExistingNonEmptyWorkspace_Throws()
    {
        var iso = MakeDummyIso();
        var clash = Path.Combine(_root, "Taken");
        Directory.CreateDirectory(clash);
        File.WriteAllText(Path.Combine(clash, "x.txt"), "occupied");
        Assert.Throws<IOException>(() => _service.CreateProject("Taken", _root, iso));
    }

    [Fact]
    public void SaveAndOpen_RoundTripsProjectData()
    {
        var iso = MakeDummyIso();
        var project = _service.CreateProject("RoundTrip", _root, iso);
        project.Settings.OutputFileName = "Custom-11.iso";
        project.Metadata["test-key"] = "test-value";
        project.Source.Sha256 = "abc123";
        _service.SaveProject(project);

        var reopened = _service.OpenProject(project.ProjectFilePath);
        Assert.Equal("RoundTrip", reopened.Name);
        Assert.Equal("Custom-11.iso", reopened.Settings.OutputFileName);
        Assert.Equal("test-value", reopened.Metadata["test-key"]);
        Assert.Equal("abc123", reopened.Source.Sha256);
    }

    [Fact]
    public void OpenProject_AcceptsWorkspaceDirectory()
    {
        var iso = MakeDummyIso();
        var project = _service.CreateProject("ByDir", _root, iso);
        var reopened = _service.OpenProject(project.WorkspacePath);
        Assert.Equal("ByDir", reopened.Name);
    }

    [Fact]
    public void OpenProject_HealsMissingFolders()
    {
        var iso = MakeDummyIso();
        var project = _service.CreateProject("Heal", _root, iso);
        Directory.Delete(Path.Combine(project.WorkspacePath, "Mount"), recursive: true);

        var issues = _service.GetWorkspaceStructureIssues(project.WorkspacePath);
        Assert.Contains(issues, i => i.Contains("Mount"));

        _service.OpenProject(project.ProjectFilePath);
        Assert.True(Directory.Exists(Path.Combine(project.WorkspacePath, "Mount")));
    }

    [Fact]
    public void SaveProject_IsAtomic_SurvivesBadPath()
    {
        var iso = MakeDummyIso();
        var project = _service.CreateProject("Atomic", _root, iso);
        _service.SaveProject(project);
        var text = File.ReadAllText(project.ProjectFilePath);
        Assert.DoesNotContain(".tmp", text);
    }

    public void Dispose()
    {
        _log.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best effort cleanup
        }
    }
}
