using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WindowsCustomizer.App.Infrastructure;
using WindowsCustomizer.Core.Engines;
using WindowsCustomizer.Core.Logging;
using WindowsCustomizer.Core.Models;
using WindowsCustomizer.Core.Services;

namespace WindowsCustomizer.App.ViewModels;

// ======================================================== REGISTRY (spec 15/16) ========================================================

public sealed partial class RegistryViewModel : CustomizationPageViewModel
{
    private readonly PresetLibrary _presets;

    [ObservableProperty] private PresetDocument? _selectedPreset;
    [ObservableProperty] private string _presetInfo = "Select a preset to see exactly what it changes.";

    public RegistryViewModel(ProjectModel project, PresetLibrary presets, IAppLogger logger, IDialogService dialogs, IStatusReporter status)
        : base(project, logger, dialogs, status)
    {
        _presets = presets;
        Title = "Registry & Presets";
        Subtitle = "Offline registry tweaks for the image. Presets state exactly which values change — nothing is hidden.";
        Presets = new ObservableCollection<PresetDocument>(presets.GetAll());
    }

    public ObservableCollection<PresetDocument> Presets { get; }

    private ObservableCollection<RegistryTweakEntry>? _tweaks;
    public ObservableCollection<RegistryTweakEntry> Tweaks => _tweaks ??= new(State.RegistryTweaks);

    private void ReloadTweaks()
    {
        // Same collection instance — Clear+refill raises a Reset (safe; nothing loops back).
        Tweaks.Clear();
        foreach (var t in State.RegistryTweaks)
        {
            Tweaks.Add(t);
        }
    }

    partial void OnSelectedPresetChanged(PresetDocument? value)
    {
        if (value is null)
        {
            return;
        }

        PresetInfo =
            $"{value.Name} v{value.Version} — {value.Description}\n" +
            string.Join("\n", value.Tweaks.Select(t => $"  • [{t.Hive}\\{t.KeyPath}] {t.ValueName} = {t.Data}  ({t.Risk})")) +
            (value.ServiceChanges.Count > 0
                ? "\n" + string.Join("\n", value.ServiceChanges.Select(s => $"  • Service {s.DisplayName} → {s.Startup}"))
                : string.Empty);
    }

    [RelayCommand]
    private void ApplyPreset()
    {
        if (SelectedPreset is null)
        {
            return;
        }

        _presets.Apply(State, SelectedPreset.Id);
        Save();
        ReloadTweaks();
        Status.SetStatus($"Preset applied: {SelectedPreset.Name}");
    }

    [RelayCommand]
    private void RemovePreset()
    {
        if (SelectedPreset is null)
        {
            return;
        }

        _presets.Remove(State, SelectedPreset.Id);
        Save();
        ReloadTweaks();
    }

    [RelayCommand]
    private void ResetPresets()
    {
        _presets.Reset(State);
        Save();
        ReloadTweaks();
    }

    [RelayCommand]
    private void DuplicatePreset()
    {
        if (SelectedPreset is null)
        {
            return;
        }

        _presets.Duplicate(SelectedPreset.Id, SelectedPreset.Name + " (copy)");
        Presets.Clear();
        foreach (var p in _presets.GetAll())
        {
            Presets.Add(p);
        }
    }

    [RelayCommand]
    private void ExportPreset()
    {
        if (SelectedPreset is null)
        {
            return;
        }

        var path = Path.Combine(Project.WorkspacePath, "Config", SelectedPreset.Id + ".preset.json");
        _presets.ExportTemplate(State, path);
        Status.SetStatus($"Exported: {path}");
    }

    [RelayCommand]
    private void ImportRegFile()
    {
        var file = Dialogs.OpenFile("Import .reg file", "Registry files (*.reg)|*.reg");
        if (file is null)
        {
            return;
        }

        // The .reg is staged and merged at build time by RegistryEngine (file hash recorded).
        var entry = new RegistryTweakEntry
        {
            Category = "Imported",
            Hive = "HKLM",
            KeyPath = @"SOFTWARE\WindowsCustomizer\Imported",
            ValueName = Path.GetFileNameWithoutExtension(file),
            ValueKind = RegistryValueKind2.String,
            Data = file,
            Risk = RiskLevel.Medium,
            Description = $"Imported REG file: {Path.GetFileName(file)}"
        };
        State.RegistryTweaks.Add(entry);
        Tweaks.Add(entry);
        Save();
    }

    [RelayCommand]
    private void ExportRegFile()
    {
        var path = Path.Combine(Project.WorkspacePath, "Config", "tweaks.reg");
        File.WriteAllText(path, RegFileBuilder.Build(State.RegistryTweaks));
        Status.SetStatus($"Exported: {path}");
    }

    [RelayCommand]
    private void ToggleTweak(RegistryTweakEntry? tweak)
    {
        if (tweak is not null)
        {
            Save();
        }
    }
}

// ======================================================== SERVICES (spec 17) ========================================================

public sealed partial class ServicesViewModel : CustomizationPageViewModel
{
    [ObservableProperty] private string _warning = string.Empty;

    public ServicesViewModel(ProjectModel project, IAppLogger logger, IDialogService dialogs, IStatusReporter status)
        : base(project, logger, dialogs, status)
    {
        Title = "Windows Services";
        Subtitle = "Configure, create or delete services in the offline image. Critical services are protected by warnings.";
        SeedCommonServices();
    }

    private ObservableCollection<ServiceEntry>? _items;
    public ObservableCollection<ServiceEntry> Items => _items ??= new(State.Services);

    public Array StartupTypes => Enum.GetValues(typeof(StartupType));

    private void SeedCommonServices()
    {
        if (State.Services.Count > 0)
        {
            return;
        }

        // Well-known services (with descriptions) + critical system services that must not break.
        foreach (var s in WindowsCustomizer.Core.Services.WellKnownCatalog.Services)
        {
            State.Services.Add(New(s.Name, s.Display, s.Startup, s.Critical, s.Warning, s.Description));
        }

        State.Services.AddRange(new[]
        {
            New("Spooler", "Print Spooler", StartupType.Automatic, true, "Disabling this service breaks printing.", "Print queue."),
            New("wuauserv", "Windows Update", StartupType.Manual, true, "Disabling this service stops Windows Update from working.", "Windows Update agent."),
            New("WlanSvc", "WLAN AutoConfig", StartupType.Automatic, true, "Disabling this service may affect Windows networking.", "Wi-Fi auto configuration."),
            New("LanmanWorkstation", "Workstation", StartupType.Automatic, true, "Disabling this service breaks file sharing and network access.", "SMB client / network provider."),
            New("Dhcp", "DHCP Client", StartupType.Automatic, true, "Disabling this service breaks automatic IP configuration.", "Network address assignment."),
            New("Dnscache", "DNS Client", StartupType.Automatic, true, "Disabling this service breaks name resolution.", "DNS caching and resolution.")
        });
    }

    private static ServiceEntry New(string name, string display, StartupType def, bool critical, string warning, string description = "") =>
        new()
        {
            ServiceName = name,
            DisplayName = display,
            Startup = def,
            DefaultStartup = def,
            IsCritical = critical,
            Warning = warning,
            Risk = critical ? RiskLevel.High : RiskLevel.Medium,
            Description = string.IsNullOrEmpty(description) ? $"Startup: {def}" : description,
            Action = ServiceAction.Configure
        };

    /// <summary>Create a new service from an executable picked on this machine (copied into the image).</summary>
    [RelayCommand]
    private void AddServiceBinary()
    {
        var file = Dialogs.OpenFile("Select the service executable", "Programs (*.exe)|*.exe|All files (*.*)|*.*");
        if (file is null)
        {
            return;
        }

        var defaultName = Path.GetFileNameWithoutExtension(file);
        var name = Dialogs.AskText("Service name", "Windows service name (short, no spaces):", defaultName);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var serviceName = name.Trim();
        var entry = New(serviceName, serviceName, StartupType.Automatic, false,
            "", $"Custom service created from {Path.GetFileName(file)}");
        entry.Action = ServiceAction.Create;
        entry.SourcePath = file;
        entry.ImagePath = $@"C:\Program Files\{serviceName}\{Path.GetFileName(file)}";
        State.Services.Add(entry);
        Items.Add(entry);
        Save();
        Warning = $"Service '{serviceName}' will be created and its file copied to {entry.ImagePath}.";
    }

    /// <summary>Copy a service's support FILES into the image so they exist after Windows installs.</summary>
    [RelayCommand]
    private void AddServiceFile()
    {
        var file = Dialogs.OpenFile("Select the service file(s) to copy into the image",
            "All files (*.*)|*.*");
        if (file is null)
        {
            return;
        }

        var target = $@"C:\Program Files\{Path.GetFileNameWithoutExtension(file)}\{Path.GetFileName(file)}";
        var entry = new FileCopyEntry
        {
            SourcePath = file,
            TargetPath = target,
            IsFolder = false
        };
        State.FileCopies.Add(entry);
        Save();
        Warning = $"File will be copied into the image at {target}.";
    }

    /// <summary>Copy a whole service FOLDER into the image (dependencies, configs, plugins).</summary>
    [RelayCommand]
    private void AddServiceFolder()
    {
        var folder = Dialogs.OpenFolder("Select the service folder to copy into the image");
        if (folder is null)
        {
            return;
        }

        var target = $@"C:\Program Files\{Path.GetFileName(folder.TrimEnd('\\', '/'))}";
        var entry = new FileCopyEntry
        {
            SourcePath = folder,
            TargetPath = target,
            IsFolder = true
        };
        State.FileCopies.Add(entry);
        Save();
        Warning = $"Folder will be copied into the image at {target}.";
    }

    /// <summary>Delete an unwanted service from the image (its registry key is removed offline).</summary>
    [RelayCommand]
    private void DeleteService()
    {
        var name = Dialogs.AskText("Delete Service",
            "The exact Windows service name to delete from the image (e.g. Fax, RemoteRegistry):");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var serviceName = name.Trim();
        if (!Dialogs.Confirm("Delete service",
                $"Delete the service '{serviceName}' from the Windows image?\n\n" +
                "This removes its registration entirely. Make sure nothing depends on it — this cannot be undone inside the built image."))
        {
            return;
        }

        var entry = New(serviceName, serviceName, StartupType.Disabled, false,
            "", "Service will be DELETED from the image.");
        entry.Action = ServiceAction.Delete;
        entry.Risk = RiskLevel.Critical;
        State.Services.Add(entry);
        Items.Add(entry);
        Save();
        Warning = $"⚠ Service '{serviceName}' is marked for deletion.";
    }

    [RelayCommand]
    private void Remove(ServiceEntry? entry)
    {
        if (entry is not null)
        {
            State.Services.Remove(entry);
            Items.Remove(entry);
            Save();
        }
    }

    [RelayCommand]
    private void SetStartup(ServiceEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        if (entry.IsCritical && entry.Startup == StartupType.Disabled)
        {
            if (!Dialogs.Confirm("Critical service",
                    $"'{entry.DisplayName}' is critical.\n\n{entry.Warning}\n\nDisable it anyway?"))
            {
                entry.Startup = entry.DefaultStartup; // live row update via INPC
                return;
            }

            Warning = $"⚠ {entry.DisplayName} is disabled: {entry.Warning}";
        }

        Save();
    }
}

// ======================================================== FEATURES (spec 13/18) ========================================================

public sealed partial class FeaturesViewModel : CustomizationPageViewModel
{
    private readonly DismEngine _dism;

    [ObservableProperty] private string _loadStatus =
        "The list below shows well-known optional features (works offline). " +
        "'Load Features (DISM)' reads the exact list from your ISO (extracts + mounts it read-only once).";

    public FeaturesViewModel(ProjectModel project, DismEngine dism, IAppLogger logger, IDialogService dialogs, IStatusReporter status)
        : base(project, logger, dialogs, status)
    {
        _dism = dism;
        Title = "Windows Features & Packages";
        Subtitle = "Optional features (.NET, Hyper-V, SMB, IIS, ...) with descriptions. Check Enable or Remove.";

        // Seed the well-known feature catalog the first time so the page is never empty.
        if (State.Features.Count == 0)
        {
            foreach (var f in WindowsCustomizer.Core.Services.WellKnownCatalog.Features)
            {
                State.Features.Add(new FeatureEntry
                {
                    FeatureName = f.FeatureName,
                    DisplayName = f.DisplayName,
                    State = "Unknown",
                    Enable = false,
                    Description = f.Description
                });
            }
        }
    }

    private ObservableCollection<FeatureEntry>? _items;
    public ObservableCollection<FeatureEntry> Items => _items ??= new(State.Features);

    [RelayCommand]
    private async Task LoadFeaturesAsync()
    {
        string? mountDir = null;
        try
        {
            if (!DismEngine.IsWindowsHost)
            {
                LoadStatus = "DISM requires Windows. On Windows this button extracts the payload and lists the exact features.";
                return;
            }

            Status.SetStatus("Preparing the image for feature listing...");
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

            LoadStatus = "Reading features via DISM...";
            var features = await _dism.GetFeaturesAsync(mountDir).ConfigureAwait(true);
            foreach (var f in features)
            {
                if (Items.All(x => !string.Equals(x.FeatureName, f.FeatureName, StringComparison.OrdinalIgnoreCase)))
                {
                    var entry = new FeatureEntry
                    {
                        FeatureName = f.FeatureName,
                        DisplayName = f.FeatureName,
                        State = f.State,
                        Enable = false,
                        Description = $"Detected in the image — current state: {f.State}"
                    };
                    Items.Add(entry);
                    State.Features.Add(entry);
                }
            }

            Save();
            LoadStatus = $"{features.Count} feature(s) found in the image.";
        }
        catch (Exception ex)
        {
            Logger.Error("Features", "Failed to list features.", ex);
            LoadStatus = "Failed: " + ex.Message;
        }
        finally
        {
            if (mountDir is not null)
            {
                try { await _dism.UnmountImageAsync(mountDir, commit: false).ConfigureAwait(true); }
                catch (Exception ex) { Logger.Warn("Features", "Unmount failed: " + ex.Message); }
            }
        }
    }

    [RelayCommand]
    private void AddCustom()
    {
        var name = Dialogs.AskText("Add Feature",
            "The Windows feature name (use the DISM /Get-Feature output), e.g. Microsoft-Hyper-V-All:");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var entry = new FeatureEntry
        {
            FeatureName = name.Trim(),
            DisplayName = name.Trim(),
            Description = "Custom feature (edit the name as needed)"
        };
        State.Features.Add(entry);
        Items.Add(entry);
        Save();
    }

    [RelayCommand]
    private void Remove(FeatureEntry? entry)
    {
        if (entry is not null)
        {
            State.Features.Remove(entry);
            Items.Remove(entry);
            Save();
        }
    }

    [RelayCommand]
    private void Toggle(FeatureEntry? entry)
    {
        if (entry is not null)
        {
            Save();
        }
    }
}
