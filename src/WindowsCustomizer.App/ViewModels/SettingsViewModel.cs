using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WindowsCustomizer.App.Infrastructure;
using WindowsCustomizer.Core.Logging;
using WindowsCustomizer.Core.Services;
using WindowsCustomizer.Core.Utilities;

namespace WindowsCustomizer.App.ViewModels;

/// <summary>Settings page: default workspace location, mode, recents, about. (Light theme is fixed.)</summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly AppSettingsStore _store;
    private readonly AppSettings _settings;
    private readonly RecentProjectsService _recent;
    private readonly AppPaths _appPaths;
    private readonly IAppLogger _logger;
    private readonly IDialogService _dialogs;
    private readonly IStatusReporter _status;

    [ObservableProperty]
    private string _defaultWorkspaceRoot = string.Empty;

    [ObservableProperty]
    private bool _isBasicMode = true;

    [ObservableProperty]
    private string _dataRoot = string.Empty;

    public SettingsViewModel(
        AppSettingsStore store,
        AppSettings settings,
        RecentProjectsService recent,
        AppPaths appPaths,
        IAppLogger logger,
        IDialogService dialogs,
        IStatusReporter status)
    {
        _store = store;
        _settings = settings;
        _recent = recent;
        _appPaths = appPaths;
        _logger = logger;
        _dialogs = dialogs;
        _status = status;

        Title = "Settings";
        Subtitle = "Application preferences. The application uses a fixed light theme.";

        DefaultWorkspaceRoot = string.IsNullOrWhiteSpace(settings.DefaultWorkspaceRoot)
            ? AppSettingsStore.DefaultWorkspaceRoot()
            : settings.DefaultWorkspaceRoot;
        IsBasicMode = settings.Mode != "Advanced";
        DataRoot = appPaths.Root;
    }

    [RelayCommand]
    private void BrowseWorkspace()
    {
        var folder = _dialogs.OpenFolder("Select the default folder for project workspaces");
        if (folder is not null)
        {
            DefaultWorkspaceRoot = folder;
        }
    }

    [RelayCommand]
    private void Save()
    {
        _settings.DefaultWorkspaceRoot = DefaultWorkspaceRoot;
        _settings.Mode = IsBasicMode ? "Basic" : "Advanced";
        _store.Save(_settings);
        _logger.Info("Settings", $"Settings saved (mode: {_settings.Mode}).");
        _status.SetStatus("Settings saved.");
        _dialogs.ShowInfo("Settings", "Settings were saved.");
    }

    [RelayCommand]
    private void ClearRecents()
    {
        if (_dialogs.Confirm("Clear Recent Projects", "Remove all entries from the recent projects list?\n(Project files are not deleted.)"))
        {
            _recent.Clear();
            _status.SetStatus("Recent projects cleared.");
        }
    }

    [RelayCommand]
    private void ToggleMode()
    {
        if (!IsBasicMode && _settings.ShowAdvancedToolsWarning)
        {
            var ok = _dialogs.Confirm(
                "Advanced Mode",
                "Advanced Mode exposes powerful tools (registry editing, DISM packages, services, custom scripts).\n\n" +
                "Mistakes there can produce an unbootable Windows image. Every change is still reviewed and risk-rated before building.\n\n" +
                "Enable Advanced Mode?");
            if (!ok)
            {
                IsBasicMode = true;
                return;
            }
        }

        _settings.Mode = IsBasicMode ? "Basic" : "Advanced";
        _store.Save(_settings);
    }

    [RelayCommand]
    private void ShowAbout()
    {
        _dialogs.ShowInfo(
            "About Windows Customizer Studio",
            $"{AppInfo.ProductName} v{AppInfo.Version}\n\n" +
            "A step-by-step Windows image customization platform:\n" +
            "ISO → Analyze → Customize → Validate → Build → Test → Final ISO\n\n" +
            "See docs\\Roadmap.md for the full feature map.");
    }
}
