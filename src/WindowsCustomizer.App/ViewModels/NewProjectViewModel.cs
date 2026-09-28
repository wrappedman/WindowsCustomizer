using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WindowsCustomizer.App.Infrastructure;
using WindowsCustomizer.Core.Logging;
using WindowsCustomizer.Core.Models;
using WindowsCustomizer.Core.Services;

namespace WindowsCustomizer.App.ViewModels;

/// <summary>New Project wizard page: name + workspace location + source ISO, with validation.</summary>
public sealed partial class NewProjectViewModel : ViewModelBase
{
    private readonly IDialogService _dialogs;
    private readonly IProjectService _projects;
    private readonly RecentProjectsService _recent;
    private readonly IAppLogger _logger;
    private readonly AppSettings _settings;
    private readonly IStatusReporter _status;
    private readonly Action<ProjectModel> _onCreated;
    private readonly Action _onCancel;

    [ObservableProperty]
    private string _projectName = string.Empty;

    [ObservableProperty]
    private string _workspaceParent = string.Empty;

    [ObservableProperty]
    private string _sourceIsoPath = string.Empty;

    [ObservableProperty]
    private string _validationMessage = string.Empty;

    [ObservableProperty]
    private bool _hasValidationMessage;

    [ObservableProperty]
    private string _isoInfoLine = "No ISO selected.";

    [ObservableProperty]
    private string _freeSpaceLine = string.Empty;

    [ObservableProperty]
    private bool _isCreating;

    public NewProjectViewModel(
        IDialogService dialogs,
        IProjectService projects,
        RecentProjectsService recent,
        IAppLogger logger,
        AppSettings settings,
        IStatusReporter status,
        Action<ProjectModel> onCreated,
        Action onCancel)
    {
        _dialogs = dialogs;
        _projects = projects;
        _recent = recent;
        _logger = logger;
        _settings = settings;
        _status = status;
        _onCreated = onCreated;
        _onCancel = onCancel;

        Title = "New Project";
        Subtitle = "Choose a name and the Windows ISO to customize. The original ISO is never modified — all work happens inside a project workspace.";

        WorkspaceParent = string.IsNullOrWhiteSpace(settings.DefaultWorkspaceRoot)
            ? AppSettingsStore.DefaultWorkspaceRoot()
            : settings.DefaultWorkspaceRoot;
    }

    [RelayCommand]
    private void BrowseIso()
    {
        var path = _dialogs.OpenFile(
            "Select the Windows installation ISO",
            "ISO images (*.iso)|*.iso|All files (*.*)|*.*");
        if (path is null)
        {
            return;
        }

        SourceIsoPath = path;
        UpdateIsoInfo();
    }

    [RelayCommand]
    private void BrowseWorkspace()
    {
        var folder = _dialogs.OpenFolder("Select the folder where projects are stored");
        if (folder is not null)
        {
            WorkspaceParent = folder;
        }
    }

    partial void OnSourceIsoPathChanged(string value) => UpdateIsoInfo();

    partial void OnWorkspaceParentChanged(string value) => UpdateFreeSpace();

    private void UpdateIsoInfo()
    {
        if (string.IsNullOrWhiteSpace(SourceIsoPath) || !File.Exists(SourceIsoPath))
        {
            IsoInfoLine = "No ISO selected.";
            return;
        }

        var info = new FileInfo(SourceIsoPath);
        IsoInfoLine = $"{info.Name}  ·  {FormatBytes(info.Length)}  ·  modified {info.LastWriteTime:yyyy-MM-dd}";
        UpdateFreeSpace();
    }

    private void UpdateFreeSpace()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(WorkspaceParent))
            {
                FreeSpaceLine = string.Empty;
                return;
            }

            var root = Path.GetPathRoot(Path.GetFullPath(WorkspaceParent));
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                FreeSpaceLine = string.Empty;
                return;
            }

            var drive = new DriveInfo(root);
            FreeSpaceLine = $"Free space on {root}: {FormatBytes(drive.AvailableFreeSpace)} (building an image typically needs 2-3× the ISO size)";
        }
        catch
        {
            FreeSpaceLine = string.Empty;
        }
    }

    [RelayCommand]
    private async Task CreateProjectAsync()
    {
        if (IsCreating)
        {
            return;
        }

        if (!Validate())
        {
            return;
        }

        IsCreating = true;
        _status.SetStatus("Creating project workspace...");
        _status.SetProgress(10, true);

        try
        {
            var project = await Task.Run(() =>
                _projects.CreateProject(ProjectName, WorkspaceParent, SourceIsoPath)).ConfigureAwait(true);

            _recent.Add(project);
            _status.SetProgress(100, false);
            _status.SetStatus($"Project '{project.Name}' created. Now analyzing the source ISO...");
            _logger.Info("NewProject", $"Project '{project.Name}' created for ISO '{project.Source.FileName}'.");
            _onCreated(project);
        }
        catch (Exception ex)
        {
            _status.SetProgress(0, false);
            _status.SetStatus("Project creation failed.");
            _logger.Error("NewProject", "Failed to create project.", ex);
            _dialogs.ShowError("New Project", UserError.FromException(
                ex, "The project workspace could not be created.",
                "Pick a different location, make sure the name is unique and that you have write permission."));
            ShowValidation(ex.Message);
        }
        finally
        {
            IsCreating = false;
        }
    }

    [RelayCommand]
    private void Cancel() => _onCancel();

    private bool Validate()
    {
        if (!Core.Utilities.PathGuard.IsValidProjectName(ProjectName, out var nameError))
        {
            ShowValidation(nameError!);
            return false;
        }

        if (string.IsNullOrWhiteSpace(SourceIsoPath))
        {
            ShowValidation("Please select the Windows ISO file to customize.");
            return false;
        }

        if (!File.Exists(SourceIsoPath))
        {
            ShowValidation($"The ISO file does not exist: {SourceIsoPath}");
            return false;
        }

        if (string.IsNullOrWhiteSpace(WorkspaceParent))
        {
            ShowValidation("Please choose the folder where project workspaces are stored.");
            return false;
        }

        var target = Path.Combine(WorkspaceParent, ProjectName.Trim());
        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
        {
            ShowValidation($"A non-empty folder named '{ProjectName.Trim()}' already exists there. Choose another name or location.");
            return false;
        }

        ClearValidation();
        return true;
    }

    private void ShowValidation(string message)
    {
        ValidationMessage = message;
        HasValidationMessage = true;
    }

    private void ClearValidation()
    {
        ValidationMessage = string.Empty;
        HasValidationMessage = false;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }
}
