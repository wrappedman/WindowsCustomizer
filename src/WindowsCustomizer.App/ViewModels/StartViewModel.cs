using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using WindowsCustomizer.App.Infrastructure;
using WindowsCustomizer.Core.Logging;
using WindowsCustomizer.Core.Models;
using WindowsCustomizer.Core.Services;

namespace WindowsCustomizer.App.ViewModels;

/// <summary>Start page: New Project, Open, Recent Projects, Settings, Help.</summary>
public sealed partial class StartViewModel : ViewModelBase
{
    private readonly IDialogService _dialogs;
    private readonly IProjectService _projects;
    private readonly RecentProjectsService _recent;
    private readonly AppSettingsStore _settingsStore;
    private readonly AppSettings _settings;
    private readonly IAppLogger _logger;
    private readonly IStatusReporter _status;
    private readonly Action _showNewProject;
    private readonly Action<ProjectModel> _openProjectView;
    private readonly Action _showSettings;

    public StartViewModel(
        IDialogService dialogs,
        IProjectService projects,
        RecentProjectsService recent,
        AppSettingsStore settingsStore,
        AppSettings settings,
        IAppLogger logger,
        IStatusReporter status,
        Action showNewProject,
        Action<ProjectModel> openProjectView,
        Action showSettings)
    {
        _dialogs = dialogs;
        _projects = projects;
        _recent = recent;
        _settingsStore = settingsStore;
        _settings = settings;
        _logger = logger;
        _status = status;
        _showNewProject = showNewProject;
        _openProjectView = openProjectView;
        _showSettings = showSettings;

        Title = "Windows Customizer Studio";
        Subtitle = "Turn a stock Windows ISO into a customized, bootable installation image — step by step, without touching DISM, registry or XML by hand.";

        RefreshRecents();
    }

    public ObservableCollection<RecentProjectItem> RecentProjects { get; } = new();

    public bool HasRecents => RecentProjects.Count > 0;

    [RelayCommand]
    private void NewProject() => _showNewProject();

    [RelayCommand]
    private void OpenProject()
    {
        try
        {
            var path = _dialogs.OpenFile(
                "Open an existing project",
                "Windows Customizer Project (*.wcp)|*.wcp|All files (*.*)|*.*");
            if (path is null)
            {
                return;
            }

            var project = _projects.OpenProject(path);
            _recent.Add(project);
            _status.SetStatus($"Opened project '{project.Name}'.");
            _openProjectView(project);
        }
        catch (Exception ex)
        {
            _logger.Error("Start", "Failed to open project.", ex);
            _dialogs.ShowError("Open Project", UserError.FromException(ex, "The project could not be opened."));
        }
    }

    [RelayCommand]
    private void OpenRecent(RecentProjectItem? item)
    {
        if (item is null)
        {
            return;
        }

        try
        {
            var project = _projects.OpenProject(item.ProjectFile);
            _recent.Add(project);
            _status.SetStatus($"Opened project '{project.Name}'.");
            _openProjectView(project);
        }
        catch (Exception ex)
        {
            _logger.Error("Start", $"Failed to open recent project '{item.Name}'.", ex);
            _dialogs.ShowError("Open Project", UserError.FromException(
                ex, $"The recent project '{item.Name}' could not be opened.",
                "The project file may have been moved or deleted. Remove it from the list and open it manually if it still exists."));
            RefreshRecents();
        }
    }

    [RelayCommand]
    private void RemoveRecent(RecentProjectItem? item)
    {
        if (item is null)
        {
            return;
        }

        _recent.Remove(item.ProjectFile);
        RefreshRecents();
    }

    [RelayCommand]
    private void OpenSettings() => _showSettings();

    [RelayCommand]
    private void OpenHelp()
    {
        _dialogs.ShowInfo(
            "Documentation",
            "Documentation lives in the 'docs' folder of the project:\n\n" +
            "  • README.md — overview and quick start\n" +
            "  • docs\\Architecture.md — technical architecture\n" +
            "  • docs\\Setup.md — build and run instructions\n" +
            "  • docs\\Roadmap.md — phase plan and status\n\n" +
            $"Version: {AppInfo.Version} (Phase 1 — ISO detection)");
    }

    private void RefreshRecents()
    {
        RecentProjects.Clear();
        foreach (var item in _recent.Load())
        {
            RecentProjects.Add(item);
        }

        OnPropertyChanged(nameof(HasRecents));
    }
}
