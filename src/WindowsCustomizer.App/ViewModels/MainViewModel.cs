using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WindowsCustomizer.App.Infrastructure;
using WindowsCustomizer.Core.Engines;
using WindowsCustomizer.Core.Logging;
using WindowsCustomizer.Core.Models;
using WindowsCustomizer.Core.Services;
using WindowsCustomizer.Core.Utilities;

namespace WindowsCustomizer.App.ViewModels;

/// <summary>
/// Application shell state: navigation, status/progress, log panel. Owns page construction but
/// never performs engine work itself — pages talk to Core services through their own VMs.
/// </summary>
public sealed partial class MainViewModel : ViewModelBase, IStatusReporter
{
    private readonly AppPaths _appPaths;
    private readonly AppSettingsStore _settingsStore;
    private readonly AppSettings _settings;
    private readonly IAppLogger _logger;
    private readonly IProjectService _projectService;
    private readonly RecentProjectsService _recentProjects;
    private readonly IIsoAnalyzer _isoAnalyzer;
    private readonly DismEngine _dismEngine;
    private readonly RegistryEngine _registryEngine;
    private readonly IsoBuilder _isoBuilder;
    private readonly PresetLibrary _presetLibrary;
    private readonly ProjectSnapshotService _snapshotService;
    private readonly ValidationEngine _validationEngine;
    private readonly DependencyEngine _dependencyEngine;
    private readonly BuildPipeline _buildPipeline;
    private readonly IDialogService _dialogs;

    [ObservableProperty]
    private ViewModelBase? _currentViewModel;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private bool _isProgressVisible;

    [ObservableProperty]
    private bool _isLogPanelOpen;

    [ObservableProperty]
    private string _adminStatusText = string.Empty;

    [ObservableProperty]
    private string _logToggleText = "Logs";

    [ObservableProperty]
    private string _logFilePath = string.Empty;

    public MainViewModel(
        AppPaths appPaths,
        AppSettingsStore settingsStore,
        AppSettings settings,
        IAppLogger logger,
        IProjectService projectService,
        RecentProjectsService recentProjects,
        IIsoAnalyzer isoAnalyzer,
        DismEngine dismEngine,
        RegistryEngine registryEngine,
        IsoBuilder isoBuilder,
        PresetLibrary presetLibrary,
        ProjectSnapshotService snapshotService,
        ValidationEngine validationEngine,
        DependencyEngine dependencyEngine,
        BuildPipeline buildPipeline,
        IDialogService dialogs)
    {
        _appPaths = appPaths;
        _settingsStore = settingsStore;
        _settings = settings;
        _logger = logger;
        _projectService = projectService;
        _recentProjects = recentProjects;
        _isoAnalyzer = isoAnalyzer;
        _dismEngine = dismEngine;
        _registryEngine = registryEngine;
        _isoBuilder = isoBuilder;
        _presetLibrary = presetLibrary;
        _snapshotService = snapshotService;
        _validationEngine = validationEngine;
        _dependencyEngine = dependencyEngine;
        _buildPipeline = buildPipeline;
        _dialogs = dialogs;

        VersionText = $"{AppInfo.ProductName} · v{AppInfo.Version}";
        LogFilePath = logger.LogFilePath ?? "(in-memory logging only)";

        BuildNavigation();
        UpdateAdminStatus();

        _logger.EntryLogged += OnEntryLogged;
    }

    public string VersionText { get; }

    public ObservableCollection<NavSectionViewModel> Navigation { get; } = new();

    public ObservableCollection<LogEntry> LogEntries { get; } = new();

    /// <summary>The project currently loaded in this session (null on the start page).</summary>
    public ProjectModel? CurrentProject { get; private set; }

    /// <summary>Called once after the window is shown.</summary>
    public void Initialize()
    {
        ShowStartPage();
        SetStatus("Ready — create a new project or open an existing one.");
    }

    // ------------------------------------------------ navigation ------------------------------------------------

    private void BuildNavigation()
    {
        Navigation.Clear();

        Navigation.Add(new NavSectionViewModel("PROJECT", new[]
        {
            new NavItemViewModel("start", "Start", "\uE80F", "PROJECT", true, ""),
            new NavItemViewModel("source", "Source ISO", "\uE7B8", "PROJECT", true, ""),
            new NavItemViewModel("edition", "Edition", "\uE895", "PROJECT", true, "")
        }));

        Navigation.Add(new NavSectionViewModel("CUSTOMIZE", new[]
        {
            new NavItemViewModel("users", "Users", "\uE77B", "CUSTOMIZE", true, ""),
            new NavItemViewModel("apps", "Apps", "\uE71D", "CUSTOMIZE", true, ""),
            new NavItemViewModel("drivers", "Drivers", "\uE950", "CUSTOMIZE", true, ""),
            new NavItemViewModel("updates", "Updates", "\uE895", "CUSTOMIZE", true, ""),
            new NavItemViewModel("registry", "Registry", "\uE943", "CUSTOMIZE", true, ""),
            new NavItemViewModel("services", "Services", "\uE90F", "CUSTOMIZE", true, ""),
            new NavItemViewModel("features", "Features", "\uE90E", "CUSTOMIZE", true, ""),
            new NavItemViewModel("scripts", "Scripts", "\uE943", "CUSTOMIZE", true, ""),
            new NavItemViewModel("branding", "Branding", "\uE722", "CUSTOMIZE", true, "")
        }));

        Navigation.Add(new NavSectionViewModel("REVIEW", new[]
        {
            new NavItemViewModel("changes", "Changes", "\uE73E", "REVIEW", true, "")
        }));

        Navigation.Add(new NavSectionViewModel("BUILD", new[]
        {
            new NavItemViewModel("build", "Build", "\uE768", "BUILD", true, ""),
            new NavItemViewModel("logs", "Logs", "\uE9D5", "BUILD", true, ""),
            new NavItemViewModel("about", "About", "\uE946", "ABOUT", true, "")
        }));
    }

    [RelayCommand]
    private void GoHome() => ShowStartPage();

    public void NavigateTo(NavItemViewModel nav)
    {
        try
        {
            // All customize/review/build pages need a loaded project.
            if (CurrentProject is null && nav.Key is not ("start" or "logs" or "about"))
            {
                ShowNewProject();
                SetStatus("Create or open a project first.");
                return;
            }

            switch (nav.Key)
            {
                case "start":
                    ShowStartPage();
                    break;
                case "source":
                    ShowIsoAnalysis(CurrentProject!);
                    break;
                case "edition":
                    ShowEdition();
                    break;
                case "users":
                    ShowUsers();
                    break;
                case "apps":
                    ShowApps();
                    break;
                case "drivers":
                    ShowDrivers();
                    break;
                case "updates":
                    ShowUpdates();
                    break;
                case "registry":
                    ShowRegistry();
                    break;
                case "services":
                    ShowServices();
                    break;
                case "features":
                    ShowFeatures();
                    break;
                case "scripts":
                    ShowScripts();
                    break;
                case "branding":
                    ShowBranding();
                    break;
                case "changes":
                case "warnings":
                    ShowReview();
                    break;
            case "build":
            case "output":
                ShowBuild();
                break;
            case "about":
                ShowAbout();
                break;
            case "logs":
                IsLogPanelOpen = !IsLogPanelOpen;
                break;
            }

            SetSelected(nav.Key);
        }
        catch (Exception ex)
        {
            // Navigation must never take the app down — explain and stay on the current page.
            _logger.Error("Navigation", $"Failed to open '{nav.Key}'.", ex);
            SetStatus($"Could not open '{nav.Title}': {ex.Message}");
            _dialogs.ShowError("Navigation", UserError.FromException(
                ex, $"The '{nav.Title}' page could not be opened.",
                "Check the log for details and try again."));
        }
    }

    private void SetSelected(string key)
    {
        foreach (var section in Navigation)
        {
            foreach (var item in section.Items)
            {
                item.IsSelected = item.Key == key;
            }
        }
    }

    private void ShowPage(ViewModelBase vm) => CurrentViewModel = vm;

    public void ShowStartPage()
    {
        ShowPage(new StartViewModel(
            _dialogs, _projectService, _recentProjects, _settingsStore, _settings, _logger, this,
            () => ShowNewProject(), ShowIsoAnalysis, ShowSettings));
        SetSelected("start");
    }

    private void ShowNewProject()
    {
        ShowPage(new NewProjectViewModel(
            _dialogs, _projectService, _recentProjects, _logger, _settings, this,
            project => { CurrentProject = project; ShowIsoAnalysis(project); },
            ShowStartPage));
        SetSelected("source");
    }

    public void ShowIsoAnalysis(ProjectModel project)
    {
        CurrentProject = project;
        ShowPage(new IsoAnalysisViewModel(project, _isoAnalyzer, _recentProjects, _logger, _dialogs, this, ShowStartPage));
        SetSelected("source");
    }

    private void ShowEdition() => ShowPage(new EditionViewModel(CurrentProject!, _dismEngine, _logger, _dialogs, this));
    private void ShowUsers() => ShowPage(new UsersViewModel(CurrentProject!, _logger, _dialogs, this));
    private void ShowApps() => ShowPage(new AppsViewModel(CurrentProject!, _dismEngine, _logger, _dialogs, this));
    private void ShowDrivers() => ShowPage(new DriversViewModel(CurrentProject!, _logger, _dialogs, this));
    private void ShowUpdates() => ShowPage(new UpdatesViewModel(CurrentProject!, _logger, _dialogs, this));
    private void ShowRegistry() => ShowPage(new RegistryViewModel(CurrentProject!, _presetLibrary, _logger, _dialogs, this));
    private void ShowServices() => ShowPage(new ServicesViewModel(CurrentProject!, _logger, _dialogs, this));
    private void ShowFeatures() => ShowPage(new FeaturesViewModel(CurrentProject!, _dismEngine, _logger, _dialogs, this));
    private void ShowScripts() => ShowPage(new ScriptsViewModel(CurrentProject!, _logger, _dialogs, this));
    private void ShowBranding() => ShowPage(new BrandingViewModel(CurrentProject!, _logger, _dialogs, this));

    private void ShowReview() => ShowPage(new ReviewViewModel(
        CurrentProject!, _dependencyEngine, _snapshotService, _logger, _dialogs, this));

    private void ShowBuild() => ShowPage(new BuildViewModel(
        CurrentProject!, _buildPipeline, _dependencyEngine, _snapshotService, _validationEngine, _logger, _dialogs, this));

    private void ShowSettings()
    {
        ShowPage(new SettingsViewModel(_settingsStore, _settings, _recentProjects, _appPaths, _logger, _dialogs, this));
        SetSelected(string.Empty);
    }

    private void ShowAbout()
    {
        ShowPage(new AboutViewModel());
        SetSelected("about");
    }

    // ------------------------------------------------ status / progress ------------------------------------------------

    public void SetStatus(string text) => StatusText = text;

    public void SetProgress(double percent, bool visible = true)
    {
        IsProgressVisible = visible;
        ProgressValue = Math.Clamp(percent, 0, 100);
    }

    // ------------------------------------------------ log panel ------------------------------------------------

    private void OnEntryLogged(object? sender, LogEntry entry)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            AppendLog(entry);
        }
        else
        {
            dispatcher.Invoke(() => AppendLog(entry));
        }
    }

    private void AppendLog(LogEntry entry)
    {
        LogEntries.Add(entry);
        while (LogEntries.Count > 500)
        {
            LogEntries.RemoveAt(0);
        }

        LogToggleText = $"Logs ({LogEntries.Count})";
    }

    [RelayCommand]
    private void ToggleLogPanel() => IsLogPanelOpen = !IsLogPanelOpen;

    [RelayCommand]
    private void CloseLogPanel() => IsLogPanelOpen = false;

    private void UpdateAdminStatus()
    {
        AdminStatusText = PrivilegeService.IsAdministrator
            ? "Running as Administrator"
            : "Standard user — servicing operations will require elevation.";
    }
}
