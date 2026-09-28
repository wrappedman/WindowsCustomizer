using System.Text.Json.Serialization;

namespace WindowsCustomizer.Core.Models;

// =================================================================================================
// Central customization state. Everything the user configures lives here and is persisted inside the
// .wcp project file. Every entry carries a Risk level and an Enabled flag so the Review page can
// toggle individual changes and the Safety system can demand confirmation (spec 27/29).
// =================================================================================================

public enum RiskLevel { Safe = 0, Low = 1, Medium = 2, High = 3, Critical = 4 }

public enum StartupType { Automatic = 0, AutomaticDelayed = 1, Manual = 2, Disabled = 3 }

public enum AppRemovalMode { Keep = 0, Remove = 1, Optional = 2 }

public enum ScriptRunPhase
{
    BeforeInstallation = 0,   // runs inside setup (RunSynchronous)
    DuringInstallation = 1,   // specialize pass
    FirstBoot = 2,            // SetupComplete.cmd / FirstLogon
    FirstUserLogin = 3,       // FirstLogonCommands
    EveryBoot = 4             // scheduled task
}

public enum RegistryValueKind2 { String = 0, ExpandString = 1, Binary = 2, Dword = 3, MultiString = 4, Qword = 5 }

/// <summary>What a service entry does to the offline image.</summary>
public enum ServiceAction
{
    /// <summary>Change the startup type of an existing service.</summary>
    Configure = 0,

    /// <summary>Create (install) a new service whose files are copied into the image.</summary>
    Create = 1,

    /// <summary>Delete an unwanted service from the image.</summary>
    Delete = 2
}

/// <summary>Base for every user-selectable change (reviewable + risk-rated). Raises change
/// notifications so list rows update live without recreating collections (crash-safe).</summary>
public abstract class ChangeEntryBase : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    protected void Raise(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, string name)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Raise(name);
        return true;
    }

    private bool _enabled = true;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public bool Enabled
    {
        get => _enabled;
        set => Set(ref _enabled, value, nameof(Enabled));
    }

    public RiskLevel Risk { get; set; } = RiskLevel.Safe;
    public string Description { get; set; } = string.Empty;
}

// ---------------------------------------------- User (spec 9) ----------------------------------------------

public sealed class UserSettings
{
    public string DefaultUsername { get; set; } = "User";
    public string DisplayName { get; set; } = "User";
    public string ComputerName { get; set; } = "WIN-CUSTOM";
    public string Organization { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool AutoLogin { get; set; }
    public bool CreateAdministrator { get; set; }
    public bool EnableGuest { get; set; }
    public string DefaultLanguage { get; set; } = "en-US";
    public string TimeZone { get; set; } = "UTC";

    /// <summary>Primary keyboard layout (KLID) — mirrors the first item of <see cref="KeyboardLayouts"/>.</summary>
    public string KeyboardLayout { get; set; } = "0409:00000409";

    /// <summary>All keyboard layouts to install; the FIRST one is the system default (English first).</summary>
    public List<string> KeyboardLayouts { get; set; } = new() { "0409:00000409" };

    public string Region { get; set; } = "en-US";

    [JsonIgnore]
    public bool HasPassword => !string.IsNullOrEmpty(Password);
}

// ---------------------------------------------- Unattend (spec 10) ----------------------------------------------

public sealed class UnattendSettings
{
    public bool GenerateAutounattend { get; set; } = true;
    public bool SkipEula { get; set; } = true;
    public bool SkipMachineOOBE { get; set; } = true;
    public bool SkipUserOOBE { get; set; } = true;
    public bool SkipRegionalSettings { get; set; } = true;
    public bool HideOobePrivacyPages { get; set; } = true;
    public bool DisableTelemetryDuringSetup { get; set; }
    public string ProductKey { get; set; } = string.Empty;
    public string EditionKey { get; set; } = string.Empty; // e.g. Professional
    public bool ConfigureDisk { get; set; }               // wipe + GPT/UEFI layout
    public string DiskLayout { get; set; } = "GptUefi";   // GptUefi | MbrBios
    public string PartitionMode { get; set; } = "Single"; // Single | Custom
    public long RecoveryPartitionMb { get; set; } = 1024;
}

// ---------------------------------------------- Drivers (spec 11) ----------------------------------------------

public sealed class DriverEntry : ChangeEntryBase
{
    public string SourcePath { get; set; } = string.Empty;   // .inf file or folder
    public string DisplayName { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Class { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public bool IsFolder { get; set; }
    public string Sha256 { get; set; } = string.Empty;
}

// ---------------------------------------------- Updates (spec 12) ----------------------------------------------

public sealed class UpdateEntry : ChangeEntryBase
{
    public string SourcePath { get; set; } = string.Empty;
    public string KbNumber { get; set; } = string.Empty;
    public string Architecture { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string UpdateType { get; set; } = "Update"; // Update | ServicingStack | LanguagePack | OptionalComponent
    public string Sha256 { get; set; } = string.Empty;
}

// ---------------------------------------------- App Removal (spec 14) ----------------------------------------------

public sealed class AppRemovalEntry : ChangeEntryBase
{
    public string PackageName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;

    private AppRemovalMode _mode = AppRemovalMode.Keep;
    public AppRemovalMode Mode
    {
        get => _mode;
        set => Set(ref _mode, value, nameof(Mode));
    }

    public bool IsSensitive { get; set; } // removal may break setup/shell -> warning + confirmation
}

// ---------------------------------------------- Registry (spec 15/16) ----------------------------------------------

public sealed class RegistryTweakEntry : ChangeEntryBase
{
    public string Category { get; set; } = "General";
    public string Hive { get; set; } = "HKLM"; // HKLM | HKCU | HKU | HKCR
    public string KeyPath { get; set; } = string.Empty;
    public string ValueName { get; set; } = string.Empty;
    public RegistryValueKind2 ValueKind { get; set; } = RegistryValueKind2.Dword;
    public string Data { get; set; } = "1";
    public string CurrentValue { get; set; } = string.Empty; // display-only (Review page)
}

// ---------------------------------------------- Services (spec 17) ----------------------------------------------

public sealed class ServiceEntry : ChangeEntryBase
{
    public string ServiceName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    private StartupType _startup = StartupType.Manual;
    public StartupType Startup
    {
        get => _startup;
        set => Set(ref _startup, value, nameof(Startup));
    }

    private ServiceAction _action = ServiceAction.Configure;
    public ServiceAction Action
    {
        get => _action;
        set => Set(ref _action, value, nameof(Action));
    }

    /// <summary>Host path of the service executable/folder (Create action).</summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>Target path of the service executable inside the image, e.g. C:\Program Files\MySvc\svc.exe (Create action).</summary>
    public string ImagePath { get; set; } = string.Empty;

    public StartupType DefaultStartup { get; set; } = StartupType.Manual;
    public bool IsCritical { get; set; }
    public string Warning { get; set; } = string.Empty;
}

// ---------------------------------------------- Features / Packages (spec 13/18) ----------------------------------------------

public sealed class FeatureEntry : ChangeEntryBase
{
    public string FeatureName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string State { get; set; } = "Available"; // Installed | Available | Disabled | Removed

    private bool _enable;
    public bool Enable
    {
        get => _enable;
        set => Set(ref _enable, value, nameof(Enable));
    }

    private bool _remove;
    public bool Remove
    {
        get => _remove;
        set => Set(ref _remove, value, nameof(Remove));
    }

    public string Dependencies { get; set; } = string.Empty;
}

// ---------------------------------------------- Scripts (spec 19) ----------------------------------------------

public sealed class ScriptEntry : ChangeEntryBase
{
    public string SourcePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Kind { get; set; } = "PowerShell"; // PowerShell | Batch | Cmd | Registry
    public ScriptRunPhase Phase { get; set; } = ScriptRunPhase.FirstBoot;
    public string Sha256 { get; set; } = string.Empty;

    private bool _trusted;
    public bool ExternalWarningAccepted
    {
        get => _trusted;
        set => Set(ref _trusted, value, nameof(ExternalWarningAccepted));
    }
}

// ---------------------------------------------- Custom Files (spec 20) ----------------------------------------------

public sealed class FileCopyEntry : ChangeEntryBase
{
    public string SourcePath { get; set; } = string.Empty;
    public string TargetPath { get; set; } = @"C:\Custom"; // target inside the mounted image
    public bool IsFolder { get; set; }
}

// ---------------------------------------------- Applications (spec 21) ----------------------------------------------

public sealed class AppInstallEntry : ChangeEntryBase
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string InstallerPath { get; set; } = string.Empty;
    public string SilentArguments { get; set; } = string.Empty;
    public int InstallOrder { get; set; } = 100;
    public string DependsOn { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
}

// ---------------------------------------------- Branding (spec 22) ----------------------------------------------

public sealed class BrandingSettings
{
    public string WallpaperPath { get; set; } = string.Empty;
    public string LockScreenPath { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string SupportUrl { get; set; } = string.Empty;
    public string SupportPhone { get; set; } = string.Empty;
    public string LogoPath { get; set; } = string.Empty;
}

// ---------------------------------------------- Language (spec 23) ----------------------------------------------

public sealed class LanguageSettings
{
    public string UiLanguage { get; set; } = "en-US";
    public string InputLocale { get; set; } = "en-US";
    public string SystemLocale { get; set; } = "en-US";
    public string UserLocale { get; set; } = "en-US";
    public string Currency { get; set; } = "USD";
    public string ShortDate { get; set; } = "M/d/yyyy";
    public string LongDate { get; set; } = "dddd, MMMM d, yyyy";
    public string ShortTime { get; set; } = "h:mm tt";
    public string LongTime { get; set; } = "h:mm:ss tt";
    public string TimeZone { get; set; } = "UTC";
}

// ---------------------------------------------- Root state ----------------------------------------------

/// <summary>Everything customized in a project. Serialized into the .wcp file.</summary>
public sealed class CustomizationState
{
    // Target selection (spec 7): single or multiple images.
    public string SelectionMode { get; set; } = "Single"; // Single | Multiple
    public List<int> SelectedImageIndexes { get; set; } = new();
    public List<string> SelectedEditionNames { get; set; } = new();

    public UserSettings User { get; set; } = new();
    public UnattendSettings Unattend { get; set; } = new();
    public LanguageSettings Language { get; set; } = new();
    public BrandingSettings Branding { get; set; } = new();

    public List<DriverEntry> Drivers { get; set; } = new();
    public List<UpdateEntry> Updates { get; set; } = new();
    public List<AppRemovalEntry> AppRemovals { get; set; } = new();
    public List<RegistryTweakEntry> RegistryTweaks { get; set; } = new();
    public List<ServiceEntry> Services { get; set; } = new();
    public List<FeatureEntry> Features { get; set; } = new();
    public List<ScriptEntry> Scripts { get; set; } = new();
    public List<FileCopyEntry> FileCopies { get; set; } = new();
    public List<AppInstallEntry> Applications { get; set; } = new();

    /// <summary>Applied preset ids (for Review + re-apply/reset).</summary>
    public List<string> AppliedPresets { get; set; } = new();

    // Security (spec 39): hash of every external artifact already stored per-entry.
}
