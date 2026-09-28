using WindowsCustomizer.Core.Logging;
using WindowsCustomizer.Core.Models;
using WindowsCustomizer.Core.Services;
using WindowsCustomizer.Core.Utilities;
using Xunit;

namespace WindowsCustomizer.Core.Tests;

public class RecentProjectsServiceTests : IDisposable
{
    private readonly string _root;
    private readonly AppPaths _paths;
    private readonly FileLogger _log;
    private readonly RecentProjectsService _service;

    public RecentProjectsServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "wc-rec-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
        _log = new FileLogger();
        _service = new RecentProjectsService(_paths, _log);
    }

    private ProjectModel MakeProject(string name)
    {
        var ws = Path.Combine(_root, name);
        Directory.CreateDirectory(ws);
        return new ProjectModel
        {
            Name = name,
            WorkspacePath = ws
        };
    }

    [Fact]
    public void AddAndLoad_ReturnsNewestFirst()
    {
        // .wcp must exist for the entry to survive the "still exists" filter.
        var p1 = MakeProject("One");
        File.WriteAllText(p1.ProjectFilePath, "{}");
        var p2 = MakeProject("Two");
        File.WriteAllText(p2.ProjectFilePath, "{}");

        _service.Add(p1);
        _service.Add(p2);

        var items = _service.Load();
        Assert.Equal(2, items.Count);
        Assert.Equal("Two", items[0].Name);
        Assert.Equal("One", items[1].Name);
    }

    [Fact]
    public void Load_DropsEntriesWhoseFileIsGone()
    {
        var p = MakeProject("Gone");
        File.WriteAllText(p.ProjectFilePath, "{}");
        _service.Add(p);
        File.Delete(p.ProjectFilePath);

        Assert.Empty(_service.Load());
    }

    [Fact]
    public void Add_SameProjectTwice_KeepsSingleLatestEntry()
    {
        var p = MakeProject("Dup");
        File.WriteAllText(p.ProjectFilePath, "{}");
        _service.Add(p);
        _service.Add(p);

        var items = _service.Load();
        Assert.Single(items);
    }

    [Fact]
    public void Clear_EmptiesList()
    {
        var p = MakeProject("X");
        File.WriteAllText(p.ProjectFilePath, "{}");
        _service.Add(p);
        _service.Clear();
        Assert.Empty(_service.Load());
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmpty()
    {
        Assert.Empty(_service.Load());
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
