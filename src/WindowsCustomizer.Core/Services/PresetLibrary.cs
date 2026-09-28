using System.Text.Json;
using WindowsCustomizer.Core.Logging;
using WindowsCustomizer.Core.Models;
using WindowsCustomizer.Core.Utilities;

namespace WindowsCustomizer.Core.Services;

// =================================================================================================
// Preset system (spec 8/24/25/26/45/46). Presets are data, not code: each is a versioned document
// (id, name, category, tweaks, service changes) that can be enabled/edited/duplicated/exported/
// imported/reset. Repository-based design keeps the door open for a future Preset Marketplace
// (official / community / company feeds with signatures).
// =================================================================================================

public sealed class PresetDocument
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0";
    public string Author { get; set; } = "Built-in";
    public string Signature { get; set; } = string.Empty; // marketplace-ready (spec 46)
    public bool IsBuiltIn { get; set; }
    public List<RegistryTweakEntry> Tweaks { get; set; } = new();
    public List<ServiceEntry> ServiceChanges { get; set; } = new();
}

/// <summary>Where presets come from. Local now; remote repositories plug in later.</summary>
public interface IPresetRepository
{
    IReadOnlyList<PresetDocument> GetAll();
    PresetDocument? GetById(string id);
    void Save(PresetDocument preset);
    bool Delete(string id);
}

public sealed class LocalPresetRepository : IPresetRepository
{
    private readonly List<PresetDocument> _builtIn = PresetCatalog.CreateBuiltIns();
    private readonly string _customDir;
    private readonly IAppLogger _log;

    public LocalPresetRepository(AppPaths paths, IAppLogger log)
    {
        _customDir = paths.PresetsDirectory;
        _log = log;
        Directory.CreateDirectory(_customDir);
    }

    public IReadOnlyList<PresetDocument> GetAll()
    {
        var list = new List<PresetDocument>(_builtIn);
        foreach (var file in Directory.GetFiles(_customDir, "*.preset.json"))
        {
            try
            {
                var doc = JsonSerializer.Deserialize<PresetDocument>(File.ReadAllText(file));
                if (doc is not null && !string.IsNullOrEmpty(doc.Id))
                {
                    doc.IsBuiltIn = false;
                    list.Add(doc);
                }
            }
            catch (Exception ex)
            {
                _log.Warn("Presets", $"Skipping unreadable preset '{file}': {ex.Message}");
            }
        }

        return list;
    }

    public PresetDocument? GetById(string id) => GetAll().FirstOrDefault(p => p.Id == id);

    public void Save(PresetDocument preset)
    {
        preset.IsBuiltIn = false;
        var path = Path.Combine(_customDir, preset.Id + ".preset.json");
        File.WriteAllText(path, JsonSerializer.Serialize(preset, new JsonSerializerOptions { WriteIndented = true }));
    }

    public bool Delete(string id)
    {
        var path = Path.Combine(_customDir, id + ".preset.json");
        if (File.Exists(path))
        {
            File.Delete(path);
            return true;
        }

        return false;
    }
}

/// <summary>High-level preset operations used by the UI.</summary>
public sealed class PresetLibrary
{
    private static readonly JsonSerializerOptions ExportOptions = new() { WriteIndented = true };

    private readonly IPresetRepository _repo;
    private readonly IAppLogger _log;

    public PresetLibrary(AppPaths paths, IAppLogger log)
    {
        _repo = new LocalPresetRepository(paths, log);
        _log = log;
    }

    public IReadOnlyList<PresetDocument> GetAll() => _repo.GetAll();

    public IReadOnlyList<PresetDocument> GetByCategory(string category) =>
        _repo.GetAll().Where(p => p.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>Merges a preset into the customization state (idempotent by tweak id).</summary>
    public void Apply(CustomizationState state, string presetId)
    {
        var preset = _repo.GetById(presetId)
            ?? throw new InvalidOperationException($"Preset '{presetId}' was not found.");

        foreach (var tweak in preset.Tweaks)
        {
            state.RegistryTweaks.RemoveAll(t => t.Id == tweak.Id);
            state.RegistryTweaks.Add(tweak);
        }

        foreach (var svc in preset.ServiceChanges)
        {
            var existing = state.Services.FirstOrDefault(s => s.ServiceName == svc.ServiceName);
            if (existing is null)
            {
                state.Services.Add(svc);
            }
            else
            {
                existing.Startup = svc.Startup;
                existing.Enabled = true;
            }
        }

        if (!state.AppliedPresets.Contains(presetId))
        {
            state.AppliedPresets.Add(presetId);
        }

        _log.Info("Presets", $"Applied preset '{preset.Name}' ({preset.Tweaks.Count} tweaks).");
    }

    public void Remove(CustomizationState state, string presetId)
    {
        var preset = _repo.GetById(presetId);
        if (preset is not null)
        {
            foreach (var tweak in preset.Tweaks)
            {
                state.RegistryTweaks.RemoveAll(t => t.Id == tweak.Id);
            }
        }

        state.AppliedPresets.Remove(presetId);
    }

    public void Reset(CustomizationState state)
    {
        var applied = state.AppliedPresets.ToList();
        foreach (var id in applied)
        {
            Remove(state, id);
        }

        _log.Info("Presets", "All presets removed from the project.");
    }

    /// <summary>Template export (spec 45): the whole project customization as a reusable template.</summary>
    public void ExportTemplate(CustomizationState state, string filePath)
    {
        var doc = new PresetDocument
        {
            Id = "template-" + Guid.NewGuid().ToString("N")[..8],
            Name = Path.GetFileNameWithoutExtension(filePath),
            Category = "Template",
            Description = "Exported project template",
            Version = "1.0",
            Tweaks = state.RegistryTweaks,
            ServiceChanges = state.Services
        };
        File.WriteAllText(filePath, JsonSerializer.Serialize(doc, ExportOptions));
    }

    public PresetDocument ImportTemplate(string filePath)
    {
        if (!PathGuard.IsInsideDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!, filePath) &&
            !File.Exists(filePath))
        {
            throw new FileNotFoundException("Template file not found.", filePath);
        }

        var doc = JsonSerializer.Deserialize<PresetDocument>(File.ReadAllText(filePath))
            ?? throw new InvalidDataException("The template file is not a valid preset document.");
        doc.IsBuiltIn = false;
        _repo.Save(doc);
        _log.Success("Presets", $"Imported preset '{doc.Name}' v{doc.Version}.");
        return doc;
    }

    public void Duplicate(string presetId, string newName)
    {
        var src = _repo.GetById(presetId) ?? throw new InvalidOperationException("Preset not found.");
        var copy = new PresetDocument
        {
            Id = "custom-" + Guid.NewGuid().ToString("N")[..8],
            Name = newName,
            Category = src.Category,
            Description = src.Description,
            Version = src.Version,
            Tweaks = src.Tweaks.Select(t => new RegistryTweakEntry
            {
                Category = t.Category, Hive = t.Hive, KeyPath = t.KeyPath, ValueName = t.ValueName,
                ValueKind = t.ValueKind, Data = t.Data, Risk = t.Risk, Description = t.Description,
                Enabled = t.Enabled
            }).ToList(),
            ServiceChanges = src.ServiceChanges
        };
        _repo.Save(copy);
    }
}

/// <summary>Built-in, documented registry/service presets. Every tweak states exactly what changes.</summary>
public static class PresetCatalog
{
    private static RegistryTweakEntry T(string category, string hive, string key, string name, RegistryValueKind2 kind, string data, RiskLevel risk, string desc) =>
        new()
        {
            Id = $"{category.ToLowerInvariant()}-{name.ToLowerInvariant()}-{Math.Abs((key + name).GetHashCode()):x}",
            Category = category, Hive = hive, KeyPath = key, ValueName = name,
            ValueKind = kind, Data = data, Risk = risk, Description = desc, Enabled = true
        };

    private static ServiceEntry S(string name, string display, StartupType startup, bool critical, string warning) =>
        new()
        {
            ServiceName = name, DisplayName = display, Startup = startup, DefaultStartup = startup,
            IsCritical = critical, Warning = warning, Enabled = true,
            Risk = critical ? RiskLevel.High : RiskLevel.Medium,
            Description = $"Set {display} to {startup}"
        };

    public static List<PresetDocument> CreateBuiltIns()
    {
        return new List<PresetDocument>
        {
            // ---------------------------------------------- Privacy ----------------------------------------------
            new()
            {
                Id = "privacy-balanced", Name = "Privacy — Balanced", Category = "Privacy", IsBuiltIn = true,
                Description = "Disables telemetry, advertising ID and activity history. Keeps Windows Update and Store functional.",
                Tweaks =
                {
                    T("Privacy", "HKLM", @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", RegistryValueKind2.Dword, "1", RiskLevel.Low, "Telemetry level = Basic (documented Microsoft policy)."),
                    T("Privacy", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", RegistryValueKind2.Dword, "0", RiskLevel.Safe, "Disable the advertising ID."),
                    T("Privacy", "HKLM", @"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", RegistryValueKind2.Dword, "0", RiskLevel.Safe, "Do not publish activity history."),
                    T("Privacy", "HKLM", @"SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", RegistryValueKind2.Dword, "0", RiskLevel.Safe, "Do not upload activity history.")
                }
            },
            new()
            {
                Id = "privacy-focused", Name = "Privacy — Focused", Category = "Privacy", IsBuiltIn = true,
                Description = "Maximum privacy: no telemetry, no web search in Start, no Cortana, no activity history.",
                Tweaks =
                {
                    T("Privacy", "HKLM", @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", RegistryValueKind2.Dword, "0", RiskLevel.Medium, "Telemetry = Security (Enterprise/Education only on some builds)."),
                    T("Privacy", "HKLM", @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana", RegistryValueKind2.Dword, "0", RiskLevel.Low, "Disable Cortana."),
                    T("Privacy", "HKLM", @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "DisableWebSearch", RegistryValueKind2.Dword, "1", RiskLevel.Safe, "Disable web search in Start."),
                    T("Privacy", "HKLM", @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "ConnectedSearchUseWeb", RegistryValueKind2.Dword, "0", RiskLevel.Safe, "Search never uses the web."),
                    T("Privacy", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", RegistryValueKind2.Dword, "0", RiskLevel.Safe, "Disable the advertising ID.")
                }
            },

            // ---------------------------------------------- Performance ----------------------------------------------
            new()
            {
                Id = "performance-balanced", Name = "Performance — Balanced", Category = "Performance", IsBuiltIn = true,
                Description = "Documented performance settings only: no startup delay, best-performance visual effects.",
                Tweaks =
                {
                    T("Performance", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", RegistryValueKind2.Dword, "0", RiskLevel.Safe, "Remove the startup app launch delay."),
                    T("Performance", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", RegistryValueKind2.Dword, "2", RiskLevel.Safe, "Visual effects = Adjust for best performance.")
                }
            },
            new()
            {
                Id = "performance-lowresource", Name = "Performance — Low Resource", Category = "Performance", IsBuiltIn = true,
                Description = "For weak hardware: disables hibernation, Superfetch and search indexing.",
                Tweaks =
                {
                    T("Performance", "HKLM", @"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled", RegistryValueKind2.Dword, "0", RiskLevel.Low, "Disable hibernation (frees RAM-sized disk space).")
                },
                ServiceChanges =
                {
                    S("SysMain", "SysMain (Superfetch)", StartupType.Disabled, false, ""),
                    S("WSearch", "Windows Search", StartupType.Manual, false, "Search indexing disabled — Start menu search is slower.")
                }
            },

            // ---------------------------------------------- Security ----------------------------------------------
            new()
            {
                Id = "security-enterprise", Name = "Security — Enterprise", Category = "Security", IsBuiltIn = true,
                Description = "Baseline hardening: SmartScreen on, SMBv1 off, UAC always notify, remote registry disabled.",
                Tweaks =
                {
                    T("Security", "HKLM", @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableSmartScreen", RegistryValueKind2.Dword, "1", RiskLevel.Safe, "Enable SmartScreen."),
                    T("Security", "HKLM", @"SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters", "SMB1", RegistryValueKind2.Dword, "0", RiskLevel.Low, "Disable SMBv1 (EternalBlue protection)."),
                    T("Security", "HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "ConsentPromptBehaviorAdmin", RegistryValueKind2.Dword, "2", RiskLevel.Low, "UAC = always prompt for credentials."),
                    T("Security", "HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA", RegistryValueKind2.Dword, "1", RiskLevel.Medium, "Keep UAC enabled.")
                },
                ServiceChanges =
                {
                    S("RemoteRegistry", "Remote Registry", StartupType.Disabled, false, "")
                }
            },

            // ---------------------------------------------- UI / Explorer ----------------------------------------------
            new()
            {
                Id = "ui-explorer-classic", Name = "Explorer — Classic Behavior", Category = "Windows UI", IsBuiltIn = true,
                Description = "Show file extensions and hidden files, open Explorer to This PC, disable recent files in Start.",
                Tweaks =
                {
                    T("Explorer", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "HideFileExt", RegistryValueKind2.Dword, "0", RiskLevel.Safe, "Show file name extensions."),
                    T("Explorer", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Hidden", RegistryValueKind2.Dword, "1", RiskLevel.Safe, "Show hidden files and folders."),
                    T("Explorer", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "LaunchTo", RegistryValueKind2.Dword, "1", RiskLevel.Safe, "Open File Explorer to This PC.")
                }
            },
            new()
            {
                Id = "ui-contextmenu-classic", Name = "Context Menu — Windows 10 Classic", Category = "Context Menu", IsBuiltIn = true,
                Description = "Replaces the Windows 11 compact context menu with the classic full menu (Win11 only).",
                Tweaks =
                {
                    T("ContextMenu", "HKCU", @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32", "", RegistryValueKind2.String, "", RiskLevel.Low, "Classic (full) right-click context menu.")
                }
            },
            new()
            {
                Id = "ui-taskbar-clean", Name = "Taskbar — Clean", Category = "Windows UI", IsBuiltIn = true,
                Description = "Left-aligned icons, hides Search/Widgets/Chat, shows seconds in the clock (Win11).",
                Tweaks =
                {
                    T("Taskbar", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAl", RegistryValueKind2.Dword, "0", RiskLevel.Safe, "Left-align taskbar icons."),
                    T("Taskbar", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Search", "SearchboxTaskbarMode", RegistryValueKind2.Dword, "0", RiskLevel.Safe, "Hide the taskbar search box."),
                    T("Taskbar", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarDa", RegistryValueKind2.Dword, "0", RiskLevel.Safe, "Hide Widgets button."),
                    T("Taskbar", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarMn", RegistryValueKind2.Dword, "0", RiskLevel.Safe, "Hide Chat button."),
                    T("Taskbar", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowSecondsInSystemClock", RegistryValueKind2.Dword, "1", RiskLevel.Safe, "Show seconds in the system clock.")
                }
            },
            new()
            {
                Id = "ui-startmenu-clean", Name = "Start Menu — Clean", Category = "Start Menu", IsBuiltIn = true,
                Description = "Hides the Recommended/Suggested section and tips in the Windows 11 Start menu.",
                Tweaks =
                {
                    T("StartMenu", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Start_IrisRecommendations", RegistryValueKind2.Dword, "0", RiskLevel.Safe, "Hide Recommended (suggested) items in Start."),
                    T("StartMenu", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Start_ShowClassicMode", RegistryValueKind2.Dword, "0", RiskLevel.Safe, "Keep the Windows 11 Start layout.")
                }
            },

            // ---------------------------------------------- Services ----------------------------------------------
            new()
            {
                Id = "services-privacy", Name = "Services — Privacy", Category = "Services", IsBuiltIn = true,
                Description = "Turns off diagnostics and feedback services.",
                ServiceChanges =
                {
                    S("DiagTrack", "Connected User Experiences and Telemetry", StartupType.Disabled, false, ""),
                    S("dmwappushservice", "WAP Push Message Routing", StartupType.Disabled, false, "")
                }
            },

            // ---------------------------------------------- Privacy (extended) ----------------------------------------------
            new()
            {
                Id = "privacy-no-web", Name = "Privacy — No Web & Ads", Category = "Privacy", IsBuiltIn = true,
                Description = "Stops Start menu web search, Bing suggestions, app-launch tracking and the consumer tips feed.",
                Tweaks =
                {
                    T("Privacy", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Start_TrackProgs", RegistryValueKind2.Dword, "0", RiskLevel.Safe, "Do not track which apps are used for Start recommendations."),
                    T("Privacy", "HKLM", @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "DisableWebSearch", RegistryValueKind2.Dword, "1", RiskLevel.Safe, "Start search never queries the web."),
                    T("Privacy", "HKLM", @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "ConnectedSearchUseWeb", RegistryValueKind2.Dword, "0", RiskLevel.Safe, "Disable Bing-connected search results."),
                    T("Privacy", "HKLM", @"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableSoftLanding", RegistryValueKind2.Dword, "1", RiskLevel.Safe, "No Windows tips/suggestions overlays."),
                    T("Privacy", "HKLM", @"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures", RegistryValueKind2.Dword, "1", RiskLevel.Low, "No suggested apps from the Store (consumer content)."),
                    T("Privacy", "HKLM", @"SOFTWARE\Policies\Microsoft\Windows\AppCompat", "AITEnable", RegistryValueKind2.Dword, "0", RiskLevel.Safe, "Disable application impact telemetry (Program Inventory)."),
                    T("Privacy", "HKLM", @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "DoNotShowFeedbackNotifications", RegistryValueKind2.Dword, "1", RiskLevel.Safe, "Never ask for feedback notifications.")
                }
            },
            new()
            {
                Id = "privacy-activity-off", Name = "Privacy — Activity History Off", Category = "Privacy", IsBuiltIn = true,
                Description = "Turns Timeline/activity history fully off and clears the upload policy.",
                Tweaks =
                {
                    T("Privacy", "HKLM", @"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", RegistryValueKind2.Dword, "0", RiskLevel.Safe, "Activity history is not published."),
                    T("Privacy", "HKLM", @"SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", RegistryValueKind2.Dword, "0", RiskLevel.Safe, "Activity history is never uploaded."),
                    T("Privacy", "HKLM", @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed", RegistryValueKind2.Dword, "0", RiskLevel.Safe, "Timeline activity feed disabled."),
                    T("Privacy", "HKLM", @"SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors", "DisableLocation", RegistryValueKind2.Dword, "1", RiskLevel.Low, "Disable the system location platform entirely.")
                }
            },

            // ---------------------------------------------- Performance (extended) ----------------------------------------------
            new()
            {
                Id = "performance-gaming", Name = "Performance — Gaming", Category = "Performance", IsBuiltIn = true,
                Description = "Game mode friendly: disables Game DVR background recording and background app churn.",
                Tweaks =
                {
                    T("Gaming", "HKCU", @"System\GameConfigStore", "GameDVR_Enabled", RegistryValueKind2.Dword, "0", RiskLevel.Safe, "Disable Game DVR recording (Xbox capture)."),
                    T("Gaming", "HKLM", @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", RegistryValueKind2.Dword, "0", RiskLevel.Safe, "Policy: Game DVR fully off."),
                    T("Gaming", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", RegistryValueKind2.Dword, "0", RiskLevel.Safe, "No background capture while gaming."),
                    T("Gaming", "HKCU", @"Software\Microsoft\GameBar", "AutoGameModeEnabled", RegistryValueKind2.Dword, "1", RiskLevel.Safe, "Automatic Game Mode enabled."),
                    T("Gaming", "HKCU", @"Software\Microsoft\GameBar", "ShowStartupPanel", RegistryValueKind2.Dword, "0", RiskLevel.Safe, "Do not show the Game Bar startup panel.")
                }
            },
            new()
            {
                Id = "performance-responsive", Name = "Performance — Responsive", Category = "Performance", IsBuiltIn = true,
                Description = "Keeps apps snappy: no startup delay, prioritizes foreground apps, faster menu reveal.",
                Tweaks =
                {
                    T("Performance", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", RegistryValueKind2.Dword, "0", RiskLevel.Safe, "Remove the startup app launch delay."),
                    T("Performance", "HKCU", @"Control Panel\Desktop", "MenuShowDelay", RegistryValueKind2.String, "80", RiskLevel.Safe, "Faster menu hover delay (80 ms)."),
                    T("Performance", "HKCU", @"Control Panel\Desktop", "WaitToKillAppTimeout", RegistryValueKind2.String, "2000", RiskLevel.Low, "Close hung apps faster at shutdown (2 s)."),
                    T("Performance", "HKLM", @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", RegistryValueKind2.Dword, "38", RiskLevel.Low, "Favor foreground applications in the scheduler.")
                }
            },

            // ---------------------------------------------- Security (extended) ----------------------------------------------
            new()
            {
                Id = "security-smb", Name = "Security — SMB Hardening", Category = "Security", IsBuiltIn = true,
                Description = "SMBv1 off, SMB signing required, guest access blocked — protects against EternalBlue/WannaCry style attacks.",
                Tweaks =
                {
                    T("Security", "HKLM", @"SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters", "SMB1", RegistryValueKind2.Dword, "0", RiskLevel.Low, "Disable the SMBv1 protocol."),
                    T("Security", "HKLM", @"SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters", "RequireSecuritySignature", RegistryValueKind2.Dword, "1", RiskLevel.Low, "Require SMB packet signing."),
                    T("Security", "HKLM", @"SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters", "EnableForcedLogoff", RegistryValueKind2.Dword, "1", RiskLevel.Low, "Force logoff when the session expires."),
                    T("Security", "HKLM", @"SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters", "EnablePlainTextPassword", RegistryValueKind2.Dword, "0", RiskLevel.Safe, "Never send plaintext passwords over SMB.")
                }
            },
            new()
            {
                Id = "security-home", Name = "Security — Home Essentials", Category = "Security", IsBuiltIn = true,
                Description = "Sensible home baseline: SmartScreen on, LLMNR off, lock screen detailed status off.",
                Tweaks =
                {
                    T("Security", "HKLM", @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableSmartScreen", RegistryValueKind2.Dword, "1", RiskLevel.Safe, "SmartScreen filter enabled."),
                    T("Security", "HKLM", @"SOFTWARE\Policies\Microsoft\Windows NT\DNSClient", "EnableMulticast", RegistryValueKind2.Dword, "0", RiskLevel.Low, "Disable LLMNR name resolution (common spoofing vector)."),
                    T("Security", "HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "DontDisplayLockedUserId", RegistryValueKind2.Dword, "3", RiskLevel.Safe, "Hide user identity on the lock screen."),
                    T("Security", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoRecentDocsNetHood", RegistryValueKind2.Dword, "1", RiskLevel.Safe, "Do not track network shares in recent items.")
                }
            },

            // ---------------------------------------------- Explorer / UI (extended) ----------------------------------------------
            new()
            {
                Id = "explorer-showall", Name = "Explorer — Show Everything", Category = "Windows UI", IsBuiltIn = true,
                Description = "Extensions, hidden and system files visible; full path in the title bar; no recent/frequent files.",
                Tweaks =
                {
                    T("Explorer", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "HideFileExt", RegistryValueKind2.Dword, "0", RiskLevel.Safe, "Show file name extensions."),
                    T("Explorer", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Hidden", RegistryValueKind2.Dword, "1", RiskLevel.Safe, "Show hidden files and folders."),
                    T("Explorer", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowSuperHidden", RegistryValueKind2.Dword, "1", RiskLevel.Low, "Show protected operating system files."),
                    T("Explorer", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\CabinetState", "FullPath", RegistryValueKind2.Dword, "1", RiskLevel.Safe, "Show the full path in the Explorer title bar."),
                    T("Explorer", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Start_TrackDocs", RegistryValueKind2.Dword, "0", RiskLevel.Safe, "Do not show recent documents in Start.")
                }
            },
            new()
            {
                Id = "ui-compact", Name = "Explorer — Compact View (Win11)", Category = "Windows UI", IsBuiltIn = true,
                Description = "Windows 11 File Explorer with compact padding and the classic ribbon context defaults.",
                Tweaks =
                {
                    T("Explorer", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "UseCompactMode", RegistryValueKind2.Dword, "1", RiskLevel.Safe, "Compact item spacing in Explorer lists."),
                    T("Explorer", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "LaunchTo", RegistryValueKind2.Dword, "1", RiskLevel.Safe, "Open File Explorer to This PC."),
                    T("Explorer", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowSyncProviderNotifications", RegistryValueKind2.Dword, "0", RiskLevel.Safe, "No OneDrive/sync provider ads in Explorer.")
                }
            },
            new()
            {
                Id = "taskbar-classic", Name = "Taskbar — Minimal", Category = "Windows UI", IsBuiltIn = true,
                Description = "Small taskbar buttons, no search box, no widgets/chat, ungrouped windows on Win11 23H2+.",
                Tweaks =
                {
                    T("Taskbar", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Search", "SearchboxTaskbarMode", RegistryValueKind2.Dword, "0", RiskLevel.Safe, "Hide the taskbar search box."),
                    T("Taskbar", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarDa", RegistryValueKind2.Dword, "0", RiskLevel.Safe, "Hide Widgets button."),
                    T("Taskbar", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarMn", RegistryValueKind2.Dword, "0", RiskLevel.Safe, "Hide Chat button."),
                    T("Taskbar", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarGlomLevel", RegistryValueKind2.Dword, "2", RiskLevel.Safe, "Never combine taskbar buttons (shows labels)."),
                    T("Taskbar", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarSi", RegistryValueKind2.Dword, "0", RiskLevel.Safe, "Small taskbar buttons (Win11 pre-23H2).")
                }
            },

            // ---------------------------------------------- Services (extended) ----------------------------------------------
            new()
            {
                Id = "services-gaming", Name = "Services — Gaming Focus", Category = "Services", IsBuiltIn = true,
                Description = "Stops background noise while gaming: retail demo, phone/messaging, fax and map services off.",
                ServiceChanges =
                {
                    S("RetailDemo", "Retail Demo Service", StartupType.Disabled, false, ""),
                    S("MapsBroker", "Downloaded Maps Manager", StartupType.Disabled, false, ""),
                    S("lfsvc", "Geolocation Service", StartupType.Disabled, false, ""),
                    S("Fax", "Fax", StartupType.Disabled, false, ""),
                    S("PhoneSvc", "Phone Service", StartupType.Disabled, false, ""),
                    S("WMPNetworkSvc", "Windows Media Player Network Sharing", StartupType.Disabled, false, "")
                }
            },
            new()
            {
                Id = "services-office", Name = "Services — Office Kiosk", Category = "Services", IsBuiltIn = true,
                Description = "Quiet office machine: no telemetry, no retail demo, no Xbox services, no auto-updaters.",
                ServiceChanges =
                {
                    S("DiagTrack", "Connected User Experiences and Telemetry", StartupType.Disabled, false, ""),
                    S("dmwappushservice", "WAP Push Message Routing", StartupType.Disabled, false, ""),
                    S("XblAuthManager", "Xbox Live Auth Manager", StartupType.Disabled, false, ""),
                    S("XblGameSave", "Xbox Live Game Save", StartupType.Disabled, false, ""),
                    S("XboxNetApiSvc", "Xbox Live Networking Service", StartupType.Disabled, false, ""),
                    S("XboxGipSvc", "Xbox Accessory Management Service", StartupType.Disabled, false, ""),
                    S("RetailDemo", "Retail Demo Service", StartupType.Disabled, false, "")
                }
            }
        };
    }
}
