using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WindowsCustomizer.App.Infrastructure;
using WindowsCustomizer.Core.Engines;
using WindowsCustomizer.Core.Logging;
using WindowsCustomizer.Core.Models;
using WindowsCustomizer.Core.Services;
using WindowsCustomizer.Core.Utilities;

namespace WindowsCustomizer.App.ViewModels;

/// <summary>Shared plumbing for pages that edit the project customization state.</summary>
public abstract partial class CustomizationPageViewModel : ViewModelBase
{
    protected readonly ProjectModel Project;
    protected readonly IAppLogger Logger;
    protected readonly IDialogService Dialogs;
    protected readonly IStatusReporter Status;

    protected CustomizationPageViewModel(
        ProjectModel project, IAppLogger logger, IDialogService dialogs, IStatusReporter status)
    {
        Project = project;
        Logger = logger;
        Dialogs = dialogs;
        Status = status;
    }

    protected CustomizationState State => Project.Customization;

    /// <summary>Saves the project file (call after every change).</summary>
    protected void Save()
    {
        new ProjectService(Logger).SaveProject(Project);
    }
}

// ======================================================== EDITION (spec 7) ========================================================

public sealed partial class EditionViewModel : CustomizationPageViewModel
{
    private readonly DismEngine _dism;
    private readonly IStatusReporter _status;

    [ObservableProperty] private string _loadStatus = "Read the image list from the source WIM (requires Windows + DISM).";
    [ObservableProperty] private bool _isMultiple;

    public EditionViewModel(ProjectModel project, DismEngine dism, IAppLogger logger, IDialogService dialogs, IStatusReporter status)
        : base(project, logger, dialogs, status)
    {
        _dism = dism;
        _status = status;
        Title = "Edition Selection";
        Subtitle = "Choose which Windows edition(s) the changes apply to. Single or multiple images (spec 7).";
        IsMultiple = State.SelectionMode == "Multiple";
        Images = new ObservableCollection<DismImageInfo>();

        // Show what we already know from the ISO analysis (WIM header) — no DISM needed.
        var src = project.Source;
        if (src.ImageCount > 0)
        {
            LoadStatus = $"The install image ({src.InstallImageFormat}) contains {src.ImageCount} image(s) " +
                         $"(compression: {src.ImageCompression}). Listing the exact editions now...";
        }
        else if (string.IsNullOrWhiteSpace(src.OriginalIsoPath))
        {
            LoadStatus = "No source ISO is selected yet — open the Source ISO page and pick the ISO, then come back.";
        }

        // Auto-load silently when the page opens (never pops an error dialog on navigation).
        _ = LoadCoreAsync(showErrors: false);
    }

    public ObservableCollection<DismImageInfo> Images { get; }

    [ObservableProperty] private DismImageInfo? _selectedImage;

    [RelayCommand]
    private Task LoadImagesAsync() => LoadCoreAsync(showErrors: true);

    /// <summary>Loads the edition list. Auto-run is silent (status text only); the manual button shows errors.</summary>
    private async Task LoadCoreAsync(bool showErrors)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(Project.Source.OriginalIsoPath) || !System.IO.File.Exists(Project.Source.OriginalIsoPath))
            {
                LoadStatus = "No source ISO is available. Open the Source ISO page and select the ISO file — the editions are then listed here automatically.";
                return;
            }

            // ── Fast path: read the edition manifest straight from the ISO — no extraction, no DISM.
            _status.SetStatus("Reading the edition list from the ISO...");
            var fast = await Task.Run(TryLoadFromIsoManifest).ConfigureAwait(true);
            if (fast is { Count: > 0 })
            {
                Images.Clear();
                foreach (var img in fast)
                {
                    Images.Add(img);
                }

                LoadStatus = $"{fast.Count} edition(s) found in the ISO.";
                if (SelectedImage is null)
                {
                    SelectedImage = Images.FirstOrDefault(i => State.SelectedImageIndexes.Contains(i.Index)) ?? Images[0];
                }

                return;
            }

            // ── Fallback: extract the payload and ask DISM (needs Windows).
            _status.SetStatus("Preparing the install image for edition listing...");
            if (!DismEngine.IsWindowsHost)
            {
                LoadStatus = "DISM requires Windows. On Windows: click 'Load Images' — the payload is extracted from the ISO automatically.";
                return;
            }

            LoadStatus = "Extracting install.wim from the ISO (one-time, with progress in the status bar)...";
            var extractProgress = new Progress<string>(m => _status.SetStatus(m));
            var wim = await Task.Run(() => WindowsCustomizer.Core.Services.InstallPayloadExtractor.Ensure(Project, Logger, extractProgress)).ConfigureAwait(true);

            LoadStatus = "Reading image list via DISM...";
            var images = await _dism.GetImageInfoAsync(wim).ConfigureAwait(true);
            Images.Clear();
            foreach (var img in images)
            {
                Images.Add(img);
            }

            LoadStatus = $"{images.Count} image(s) found in {Path.GetFileName(wim)}.";
            if (Images.Count > 0 && SelectedImage is null)
            {
                SelectedImage = Images.FirstOrDefault(i => State.SelectedImageIndexes.Contains(i.Index)) ?? Images[0];
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Edition", "Failed to read image info.", ex);
            LoadStatus = "Could not list the editions: " + ex.Message;
            if (showErrors)
            {
                Dialogs.ShowError("Editions", UserError.FromException(ex, "Could not read the Windows image list."));
            }
        }
    }

    /// <summary>
    /// Instantly lists the editions inside the ISO by parsing the WIM XML manifest
    /// (uncompressed UTF-16 inside install.wim/esd) — no multi-GB extraction needed.
    /// </summary>
    private List<DismImageInfo>? TryLoadFromIsoManifest()
    {
        try
        {
            using var fsStream = new FileStream(Project.Source.OriginalIsoPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            DiscUtils.DiscFileSystem fs;
            try
            {
                fs = new DiscUtils.Udf.UdfReader(fsStream);
            }
            catch
            {
                fsStream.Seek(0, SeekOrigin.Begin);
                fs = new DiscUtils.Iso9660.CDReader(fsStream, joliet: true, hideVersions: true);
            }

            using (fs)
            {
                foreach (var name in WindowsCustomizer.Core.Services.InstallPayloadExtractor.PayloadNames)
                {
                    if (!fs.FileExists(@"\sources\" + name))
                    {
                        continue;
                    }

                    using var s = fs.OpenFile(@"\sources\" + name, FileMode.Open, FileAccess.Read);
                    var images = WindowsCustomizer.Core.Services.WimXmlReader.TryReadImages(s);
                    if (images is null)
                    {
                        continue;
                    }

                    var rows = new List<DismImageInfo>();
                    var approxEach = (long)(s.Length / Math.Max(1, images.Count) * 2.8); // WIM/ESD compresses ~3:1
                    foreach (var img in images)
                    {
                        var label = string.IsNullOrWhiteSpace(img.Description) ? img.EditionId : img.Description;
                        rows.Add(new DismImageInfo(img.Index, img.Name, label, approxEach));
                    }

                    Logger.Info("Edition", $"{rows.Count} edition(s) read from the ISO manifest ({name}).");
                    return rows;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("Edition", $"Fast manifest read failed, falling back to DISM: {ex.Message}");
        }

        return null;
    }

    [RelayCommand]
    private void SelectImage()
    {
        if (SelectedImage is null)
        {
            return;
        }

        State.SelectedImageIndexes.Clear();
        State.SelectedEditionNames.Clear();
        State.SelectedImageIndexes.Add(SelectedImage.Index);
        State.SelectedEditionNames.Add(SelectedImage.Name);
        Save();
        _status.SetStatus($"Target edition: {SelectedImage.Name} (index {SelectedImage.Index}).");
    }

    partial void OnIsMultipleChanged(bool value)
    {
        State.SelectionMode = value ? "Multiple" : "Single";
        Save();
    }

    // Payload extraction lives in Core (InstallPayloadExtractor) — shared with the Apps/Features pages.
}

// ======================================================== USERS (spec 9) ========================================================

public sealed partial class UsersViewModel : CustomizationPageViewModel
{
    public UsersViewModel(ProjectModel project, IAppLogger logger, IDialogService dialogs, IStatusReporter status)
        : base(project, logger, dialogs, status)
    {
        Title = "User & Computer";
        Subtitle = "Default account and computer identity created at first boot.";
        EnsureLayouts();
    }

    public UserSettings U => State.User;

    // Pick lists for the language/region page (editable combo boxes — selection, not free typing).
    public IReadOnlyList<string> Languages => WindowsCustomizer.Core.Services.WellKnownCatalog.Languages;
    public IReadOnlyList<string> TimeZones => WindowsCustomizer.Core.Services.WellKnownCatalog.TimeZones;
    public IReadOnlyList<string> Regions => WindowsCustomizer.Core.Services.WellKnownCatalog.Regions;

    // ------------------------------------------------ Keyboard layouts (multi-select, English default) ------------------------------------------------

    /// <summary>A selected keyboard layout row (first = system default).</summary>
    public sealed record LayoutRow(string Code, string Name, bool IsDefault);

    /// <summary>All available layouts shown with human-readable names.</summary>
    public IReadOnlyList<WindowsCustomizer.Core.Services.WellKnownCatalog.KeyboardLayoutOption> LayoutOptions =>
        WindowsCustomizer.Core.Services.WellKnownCatalog.KeyboardLayouts;

    private ObservableCollection<LayoutRow> _selectedLayouts = new();
    public ObservableCollection<LayoutRow> SelectedLayouts
    {
        get => _selectedLayouts;
        private set => SetProperty(ref _selectedLayouts, value);
    }

    /// <summary>The layout picked in the "add layout" combo box.</summary>
    private WindowsCustomizer.Core.Services.WellKnownCatalog.KeyboardLayoutOption? _layoutToAdd;
    public WindowsCustomizer.Core.Services.WellKnownCatalog.KeyboardLayoutOption? LayoutToAdd
    {
        get => _layoutToAdd;
        set => SetProperty(ref _layoutToAdd, value);
    }

    /// <summary>Loads the saved layouts (ensures at least English US as the default).</summary>
    public void EnsureLayouts()
    {
        if (SelectedLayouts.Count > 0)
        {
            return;
        }

        var codes = (U.KeyboardLayouts ?? new List<string>())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (codes.Count == 0 && !string.IsNullOrWhiteSpace(U.KeyboardLayout))
        {
            codes.Add(U.KeyboardLayout);
        }

        if (codes.Count == 0)
        {
            codes.Add("0409:00000409"); // English (US) is always the default
        }

        RebuildLayouts(codes);
    }

    private void RebuildLayouts(IReadOnlyList<string> codes)
    {
        SelectedLayouts = new ObservableCollection<LayoutRow>(
            codes.Select((c, i) => new LayoutRow(
                c,
                WindowsCustomizer.Core.Services.WellKnownCatalog.KeyboardLayoutName(c),
                i == 0)));

        // Persist: first layout = default keyboard.
        U.KeyboardLayouts = codes.ToList();
        U.KeyboardLayout = codes[0];
    }

    [RelayCommand]
    private void AddLayout()
    {
        if (LayoutToAdd is null)
        {
            Dialogs.ShowWarning("Keyboard Layout", "Pick a layout from the list first, then click Add.");
            return;
        }

        EnsureLayouts();
        if (SelectedLayouts.Any(l => string.Equals(l.Code, LayoutToAdd.Code, StringComparison.OrdinalIgnoreCase)))
        {
            Dialogs.ShowInfo("Keyboard Layout", $"'{LayoutToAdd.Name}' is already in the list.");
            return;
        }

        var codes = SelectedLayouts.Select(l => l.Code).Append(LayoutToAdd.Code).ToList();
        RebuildLayouts(codes);
        Status.SetStatus($"Keyboard layout added: {LayoutToAdd.Name}");
    }

    [RelayCommand]
    private void RemoveLayout(LayoutRow? row)
    {
        if (row is null)
        {
            return;
        }

        EnsureLayouts();
        if (SelectedLayouts.Count <= 1)
        {
            Dialogs.ShowWarning("Keyboard Layout", "At least one layout is required — English (US) stays as the default.");
            return;
        }

        RebuildLayouts(SelectedLayouts.Where(l => l != row).Select(l => l.Code).ToList());
    }

    [RelayCommand]
    private void MakeDefaultLayout(LayoutRow? row)
    {
        if (row is null)
        {
            return;
        }

        EnsureLayouts();
        var codes = new List<string> { row.Code };
        codes.AddRange(SelectedLayouts.Where(l => l != row).Select(l => l.Code));
        RebuildLayouts(codes);
        Status.SetStatus($"Default keyboard: {row.Name}");
    }

    /// <summary>Live dependency warnings shown directly on this page (never a silent surprise at build).</summary>
    public string Warnings
    {
        get
        {
            var issues = new WindowsCustomizer.Core.Services.DependencyEngine().Analyze(State)
                .Where(i => i.Severity >= WindowsCustomizer.Core.Services.IssueSeverity.Warning)
                .ToList();
            return issues.Count == 0
                ? string.Empty
                : string.Join("\n", issues.Select(i =>
                    (i.Severity == WindowsCustomizer.Core.Services.IssueSeverity.Blocker ? "⚠ " : "• ") +
                    $"{i.Message} — {i.Resolution}"));
        }
    }

    /// <summary>Generates a strong password for the default user (needed by Auto Login).</summary>
    [RelayCommand]
    private void GeneratePassword()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789!@#$%";
        var bytes = new byte[14];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        var pwd = new string(bytes.Select(b => alphabet[b % alphabet.Length]).ToArray());

        U.Password = pwd;
        Save();
        OnPropertyChanged(nameof(Warnings));
        Dialogs.ShowInfo("Password generated",
            $"A strong password was generated for '{U.DefaultUsername}':\n\n    {pwd}\n\n" +
            "Write it down now — it is also saved in the project file. " +
            "Auto Login will use it automatically at first boot.");
        Status.SetStatus("Password generated for the default user.");
    }

    [RelayCommand]
    private void SaveUsers()
    {
        // Auto Login without a password would break the unattended setup — fix it right here.
        if (U.AutoLogin && !U.HasPassword)
        {
            GeneratePassword();
        }

        Save();
        OnPropertyChanged(nameof(Warnings));
        Status.SetStatus("User settings saved.");
    }
}

// ======================================================== APPS (spec 8/14/21) ========================================================

public sealed partial class AppsViewModel : CustomizationPageViewModel
{
    private readonly DismEngine _dism;

    [ObservableProperty] private string _loadStatus =
        "The list below shows well-known pre-installed apps (works offline). " +
        "'Load from Image' reads the exact packages from your ISO (extracts + mounts it read-only once).";

    public AppsViewModel(ProjectModel project, DismEngine dism, IAppLogger logger, IDialogService dialogs, IStatusReporter status)
        : base(project, logger, dialogs, status)
    {
        _dism = dism;
        Title = "App Removal";
        Subtitle = "Keep / Remove / Optional for provisioned (pre-installed) apps. Sensitive packages are flagged.";

        // Seed the well-known catalog the first time so the page is never empty.
        if (State.AppRemovals.Count == 0)
        {
            foreach (var app in WindowsCustomizer.Core.Services.WellKnownCatalog.ProvisionedApps)
            {
                State.AppRemovals.Add(new AppRemovalEntry
                {
                    PackageName = app.PackagePattern,
                    DisplayName = app.DisplayName,
                    Mode = AppRemovalMode.Keep,
                    IsSensitive = app.Sensitive,
                    Description = app.Description
                });
            }
        }
    }

    // Bound collection: created ONCE (stable identity). Recreating collections per read causes
    // infinite layout/binding loops (stack overflow) — see bugfix notes.
    private ObservableCollection<AppRemovalEntry>? _apps;
    public ObservableCollection<AppRemovalEntry> Apps => _apps ??= new(State.AppRemovals);

    private ObservableCollection<AppInstallEntry>? _installApps;
    public ObservableCollection<AppInstallEntry> InstallApps => _installApps ??= new(State.Applications);

    private void SyncAdd(AppRemovalEntry e) { State.AppRemovals.Add(e); Apps.Add(e); }
    private void SyncRemove(AppRemovalEntry e) { State.AppRemovals.Remove(e); Apps.Remove(e); }

    [RelayCommand]
    private async Task LoadAppsAsync()
    {
        string? mountDir = null;
        try
        {
            if (!DismEngine.IsWindowsHost)
            {
                LoadStatus = "DISM requires Windows. On Windows this button extracts the payload and lists the exact packages.";
                return;
            }

            Status.SetStatus("Preparing the image for app listing...");
            LoadStatus = "Extracting install.wim from the ISO (one-time)...";
            var wim = await WindowsCustomizer.Core.Services.InstallPayloadExtractor.EnsureAsync(Project, Logger).ConfigureAwait(true);

            LoadStatus = "Mounting the image read-only (this can take a few minutes)...";
            mountDir = Path.Combine(Project.WorkspacePath, "Mount");
            Directory.CreateDirectory(mountDir);
            var mount = await _dism.MountImageAsync(wim, 1, mountDir, readOnly: true).ConfigureAwait(true);
            if (!mount.Success)
            {
                LoadStatus = $"Could not mount the image (exit {mount.ExitCode}). The well-known list below is still available.";
                return;
            }

            LoadStatus = "Reading provisioned apps via DISM...";
            var apps = await _dism.GetProvisionedAppsAsync(mountDir).ConfigureAwait(true);
            foreach (var app in apps)
            {
                if (State.AppRemovals.All(a => !string.Equals(a.PackageName, app.PackageName, StringComparison.OrdinalIgnoreCase)))
                {
                    SyncAdd(new AppRemovalEntry
                    {
                        PackageName = app.PackageName,
                        DisplayName = string.IsNullOrEmpty(app.DisplayName) ? app.PackageName : app.DisplayName,
                        Version = app.Version,
                        Mode = AppRemovalMode.Keep,
                        IsSensitive = IsSensitive(app.PackageName),
                        Description = "Provisioned app detected in the image"
                    });
                }
            }

            Save();
            LoadStatus = $"{apps.Count} provisioned app(s) found in the image.";
        }
        catch (Exception ex)
        {
            Logger.Error("Apps", "Failed to list provisioned apps.", ex);
            LoadStatus = "Failed: " + ex.Message;
        }
        finally
        {
            if (mountDir is not null)
            {
                try { await _dism.UnmountImageAsync(mountDir, commit: false).ConfigureAwait(true); }
                catch (Exception ex) { Logger.Warn("Apps", "Unmount failed: " + ex.Message); }
            }
        }
    }

    /// <summary>Add an AppX/MSIX (or EXE/MSI installer) from disk to the install list.</summary>
    [RelayCommand]
    private void AddAppPackage()
    {
        var file = Dialogs.OpenFile("Select the application package",
            "Application packages (*.appx;*.msix;*.appxbundle;*.msixbundle;*.exe;*.msi)|*.appx;*.msix;*.appxbundle;*.msixbundle;*.exe;*.msi|All files (*.*)|*.*");
        if (file is null)
        {
            return;
        }

        var name = Dialogs.AskText("Application name", "Display name for this application:", Path.GetFileNameWithoutExtension(file));
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var entry = new AppInstallEntry
        {
            Name = name.Trim(),
            InstallerPath = file,
            SilentArguments = GuessSilentArgs(file),
            InstallOrder = 100 + State.Applications.Count * 10
        };
        State.Applications.Add(entry);
        InstallApps.Add(entry);
        Save();
        Status.SetStatus($"Application added: {entry.Name}");
    }

    [RelayCommand]
    private void RemoveAppPackage(AppInstallEntry? entry)
    {
        if (entry is not null)
        {
            State.Applications.Remove(entry);
            InstallApps.Remove(entry);
            Save();
        }
    }

    private static string GuessSilentArgs(string file) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".msi" => "/qn /norestart",
        ".exe" => "/S",
        _ => string.Empty // appx/msix are provisioned directly — no arguments needed
    };

    /// <summary>Add a provisioned package to remove by its package name (typed).</summary>
    [RelayCommand]
    private void AddCustom()
    {
        var name = Dialogs.AskText("Add Provisioned Package",
            "The provisioned package name to remove (e.g. Microsoft.BingWeather_4.x.x.x_neutral_~_8wekyb3d8bbwe):");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        SyncAdd(new AppRemovalEntry
        {
            PackageName = name.Trim(),
            DisplayName = name.Trim(),
            Mode = AppRemovalMode.Remove,
            Description = "Custom provisioned package name"
        });
        Save();
    }

    [RelayCommand]
    private void Remove(AppRemovalEntry? entry)
    {
        if (entry is not null)
        {
            SyncRemove(entry);
            Save();
        }
    }

    [RelayCommand]
    private void SetKeep(AppRemovalEntry? e) => SetMode(e, AppRemovalMode.Keep);

    [RelayCommand]
    private void SetRemove(AppRemovalEntry? e)
    {
        if (e is null)
        {
            return;
        }

        if (e.IsSensitive &&
            !Dialogs.Confirm("Remove sensitive package",
                $"'{e.DisplayName}' may be required by Windows Setup or the Shell.\n\nRemove it anyway?"))
        {
            return;
        }

        SetMode(e, AppRemovalMode.Remove);
    }

    [RelayCommand]
    private void SetOptional(AppRemovalEntry? e) => SetMode(e, AppRemovalMode.Optional);

    private void SetMode(AppRemovalEntry? e, AppRemovalMode mode)
    {
        if (e is null)
        {
            return;
        }

        e.Mode = mode; // row updates live via INotifyPropertyChanged — no collection refresh
        Save();
    }

    private static bool IsSensitive(string packageName) =>
        packageName.Contains("WindowsStore", StringComparison.OrdinalIgnoreCase) ||
        packageName.Contains("ShellExperience", StringComparison.OrdinalIgnoreCase) ||
        packageName.Contains("StartMenuExperience", StringComparison.OrdinalIgnoreCase) ||
        packageName.Contains("SecHealthUI", StringComparison.OrdinalIgnoreCase);
}

// ======================================================== DRIVERS (spec 11) ========================================================

public sealed partial class DriversViewModel : CustomizationPageViewModel
{
    [ObservableProperty] private string _addStatus = "Drivers are validated before injection (INF or folder).";

    public DriversViewModel(ProjectModel project, IAppLogger logger, IDialogService dialogs, IStatusReporter status)
        : base(project, logger, dialogs, status)
    {
        Title = "Drivers";
        Subtitle = "Add INF drivers or driver folders — they are injected with DISM /Add-Driver during the build.";
    }

    private ObservableCollection<DriverEntry>? _items;
    public ObservableCollection<DriverEntry> Items => _items ??= new(State.Drivers);

    [RelayCommand]
    private void AddInf()
    {
        var file = Dialogs.OpenFile("Select a driver INF", "Driver information (*.inf)|*.inf");
        if (file is null)
        {
            return;
        }

        if (!ValidateInf(file))
        {
            return;
        }

        var entry = new DriverEntry
        {
            SourcePath = file,
            DisplayName = Path.GetFileNameWithoutExtension(file),
            IsFolder = false,
            Sha256 = "recorded-at-build",
            Description = "INF driver"
        };
        State.Drivers.Add(entry);
        Items.Add(entry);
        Save();
    }

    [RelayCommand]
    private void AddFolder()
    {
        var folder = Dialogs.OpenFolder("Select a driver folder");
        if (folder is null)
        {
            return;
        }

        if (!Directory.EnumerateFiles(folder, "*.inf", SearchOption.AllDirectories).Any())
        {
            Dialogs.ShowWarning("Add Driver", "No .inf files found in that folder — it is not a valid driver package.");
            return;
        }

        var entry = new DriverEntry
        {
            SourcePath = folder,
            DisplayName = Path.GetFileName(folder),
            IsFolder = true,
            Description = "Driver package folder (recursed)"
        };
        State.Drivers.Add(entry);
        Items.Add(entry);
        Save();
    }

    [RelayCommand]
    private void Remove(DriverEntry? entry)
    {
        if (entry is not null)
        {
            State.Drivers.Remove(entry);
            Items.Remove(entry);
            Save();
        }
    }

    [RelayCommand]
    private void ExportList()
    {
        var path = Path.Combine(Project.WorkspacePath, "Logs", "driver-list.txt");
        File.WriteAllLines(path, State.Drivers.Select(d => $"{d.DisplayName}\t{d.SourcePath}\t{d.Sha256}"));
        Status.SetStatus($"Driver list exported: {path}");
    }

    private bool ValidateInf(string path)
    {
        try
        {
            var head = File.ReadLines(path).Take(20).ToList();
            if (!head.Any(l => l.Contains("[Version]", StringComparison.OrdinalIgnoreCase)))
            {
                Dialogs.ShowWarning("Add Driver", "This does not look like a valid INF file (missing [Version] section).");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Dialogs.ShowError("Add Driver", UserError.FromException(ex, "The INF file could not be validated."));
            return false;
        }
    }
}

// ======================================================== UPDATES (spec 12) ========================================================

public sealed partial class UpdatesViewModel : CustomizationPageViewModel
{
    public UpdatesViewModel(ProjectModel project, IAppLogger logger, IDialogService dialogs, IStatusReporter status)
        : base(project, logger, dialogs, status)
    {
        Title = "Updates & Packages";
        Subtitle = "Add MSU/CAB updates, language packs and optional components. Servicing Stack installs first.";
    }

    private ObservableCollection<UpdateEntry>? _items;
    public ObservableCollection<UpdateEntry> Items => _items ??= new(State.Updates);

    [RelayCommand]
    private void AddUpdate()
    {
        var file = Dialogs.OpenFile("Select an update (MSU or CAB)",
            "Windows updates (*.msu;*.cab)|*.msu;*.cab|All files (*.*)|*.*");
        if (file is null)
        {
            return;
        }

        var name = Path.GetFileNameWithoutExtension(file);
        var kb = System.Text.RegularExpressions.Regex.Match(name, @"(KB\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var entry = new UpdateEntry
        {
            SourcePath = file,
            KbNumber = kb.Success ? kb.Value.ToUpperInvariant() : "(unknown)",
            Architecture = name.Contains("arm64", StringComparison.OrdinalIgnoreCase) ? "ARM64" :
                name.Contains("x64", StringComparison.OrdinalIgnoreCase) ? "x64" : "(unknown)",
            SizeBytes = new FileInfo(file).Length,
            UpdateType = Path.GetExtension(file).Equals(".cab", StringComparison.OrdinalIgnoreCase) ? "CAB" : "Update",
            Description = Path.GetFileName(file)
        };
        State.Updates.Add(entry);
        Items.Add(entry);
        Save();
    }

    [RelayCommand]
    private void Remove(UpdateEntry? entry)
    {
        if (entry is not null)
        {
            State.Updates.Remove(entry);
            Items.Remove(entry);
            Save();
        }
    }
}

// ======================================================== SCRIPTS (spec 19) ========================================================

public sealed partial class ScriptsViewModel : CustomizationPageViewModel
{
    public ScriptsViewModel(ProjectModel project, IAppLogger logger, IDialogService dialogs, IStatusReporter status)
        : base(project, logger, dialogs, status)
    {
        Title = "Scripts";
        Subtitle = "PowerShell / Batch / CMD / Registry scripts with a defined run phase. External files must be acknowledged.";
    }

    private ObservableCollection<ScriptEntry>? _items;
    public ObservableCollection<ScriptEntry> Items => _items ??= new(State.Scripts);

    public Array Phases => Enum.GetValues(typeof(ScriptRunPhase));

    // ---------------------------------------------- Sample scripts (see before you use) ----------------------------------------------

    public IReadOnlyList<WindowsCustomizer.Core.Services.WellKnownCatalog.ScriptSample> Samples =>
        WindowsCustomizer.Core.Services.WellKnownCatalog.ScriptSamples;

    private WindowsCustomizer.Core.Services.WellKnownCatalog.ScriptSample? _selectedSample;
    public WindowsCustomizer.Core.Services.WellKnownCatalog.ScriptSample? SelectedSample
    {
        get => _selectedSample;
        set
        {
            SetProperty(ref _selectedSample, value);
            OnPropertyChanged(nameof(SampleCode));
        }
    }

    public string SampleCode => SelectedSample?.Code ?? "Select a sample on the left to preview its code.";

    /// <summary>Writes the sample into the project's Scripts folder and adds it to the list (trusted).</summary>
    [RelayCommand]
    private void AddSample()
    {
        if (SelectedSample is null)
        {
            Dialogs.ShowWarning("Samples", "Select a sample script first.");
            return;
        }

        var scriptsDir = Path.Combine(Project.WorkspacePath, "Scripts");
        Directory.CreateDirectory(scriptsDir);
        var dest = Path.Combine(scriptsDir, SelectedSample.FileName);
        File.WriteAllText(dest, SelectedSample.Code);

        var entry = new ScriptEntry
        {
            SourcePath = dest,
            FileName = SelectedSample.FileName,
            Kind = SelectedSample.Kind,
            Phase = SelectedSample.Phase,
            Description = SelectedSample.Description,
            ExternalWarningAccepted = true // built-in sample — already reviewed
        };
        State.Scripts.Add(entry);
        Items.Add(entry);
        Save();
        Status.SetStatus($"Sample added: {SelectedSample.FileName}");
    }

    [RelayCommand]
    private void AddScript()
    {
        var file = Dialogs.OpenFile("Select a script",
            "Scripts (*.ps1;*.cmd;*.bat;*.reg)|*.ps1;*.cmd;*.bat;*.reg|All files (*.*)|*.*");
        if (file is null)
        {
            return;
        }

        var kind = Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".ps1" => "PowerShell",
            ".reg" => "Registry",
            ".bat" => "Batch",
            _ => "Cmd"
        };

        var entry = new ScriptEntry
        {
            SourcePath = file,
            FileName = Path.GetFileName(file),
            Kind = kind,
            Phase = ScriptRunPhase.FirstBoot,
            Description = "External script — will be hashed and reviewed before build"
        };
        State.Scripts.Add(entry);
        Items.Add(entry);
        Save();
    }

    [RelayCommand]
    private void Trust(ScriptEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        if (Dialogs.Confirm("Trust script",
                $"Mark '{entry.FileName}' as reviewed and trusted?\n\n" +
                "Only do this if you understand exactly what the script does — it will run with elevated privileges."))
        {
            entry.ExternalWarningAccepted = true; // live row update via INPC
            Save();
        }
    }

    [RelayCommand]
    private void Remove(ScriptEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        State.Scripts.Remove(entry);
        Items.Remove(entry);
        Save();
    }
}

// ======================================================== BRANDING (spec 22) ========================================================

public sealed partial class BrandingViewModel : CustomizationPageViewModel
{
    public BrandingViewModel(ProjectModel project, IAppLogger logger, IDialogService dialogs, IStatusReporter status)
        : base(project, logger, dialogs, status)
    {
        Title = "Wallpaper & Branding";
        Subtitle = "Wallpaper, lock screen and OEM information (manufacturer, model, support).";
    }

    public BrandingSettings B => State.Branding;

    [RelayCommand]
    private void PickWallpaper()
    {
        var file = Dialogs.OpenFile("Select wallpaper image", "Images (*.jpg;*.png;*.bmp)|*.jpg;*.png;*.bmp");
        if (file is not null)
        {
            B.WallpaperPath = file;
            OnPropertyChanged(nameof(B));
        }
    }

    [RelayCommand]
    private void PickLockScreen()
    {
        var file = Dialogs.OpenFile("Select lock screen image", "Images (*.jpg;*.png;*.bmp)|*.jpg;*.png;*.bmp");
        if (file is not null)
        {
            B.LockScreenPath = file;
            OnPropertyChanged(nameof(B));
        }
    }

    [RelayCommand]
    private void PickLogo()
    {
        var file = Dialogs.OpenFile("Select OEM logo", "Images (*.png;*.bmp)|*.png;*.bmp");
        if (file is not null)
        {
            B.LogoPath = file;
            OnPropertyChanged(nameof(B));
        }
    }

    [RelayCommand]
    private void SaveBranding()
    {
        Save();
        Status.SetStatus("Branding saved.");
    }
}
