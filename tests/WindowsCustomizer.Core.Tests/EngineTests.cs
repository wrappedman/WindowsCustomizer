using System.Xml.Linq;
using WindowsCustomizer.Core.Engines;
using WindowsCustomizer.Core.Models;
using WindowsCustomizer.Core.Services;
using Xunit;

namespace WindowsCustomizer.Core.Tests;

public class UnattendGeneratorTests
{
    private static CustomizationState State()
    {
        var s = new CustomizationState();
        s.User.DefaultUsername = "Admin";
        s.User.DisplayName = "Administrator";
        s.User.Password = "P@ssw0rd!";
        s.User.ComputerName = "MY-PC";
        s.User.Organization = "Contoso";
        s.User.AutoLogin = true;
        s.User.CreateAdministrator = true;
        s.SelectedEditionNames.Add("Windows 11 Pro");
        s.Unattend.ProductKey = "VK7JG-NPHTM-C97JM-9MPGT-3V66T";
        s.Unattend.ConfigureDisk = true;
        return s;
    }

    [Fact]
    public void Generate_ProducesWellFormedXmlWithAllPasses()
    {
        var xml = new UnattendGenerator().Generate(State());
        var doc = XDocument.Parse(xml);

        var passes = doc.Root!.Elements().Select(e => e.Attribute("pass")?.Value).ToList();
        Assert.Contains("windowsPE", passes);
        Assert.Contains("specialize", passes);
        Assert.Contains("oobeSystem", passes);
    }

    [Fact]
    public void Generate_ContainsUserAndComputerSettings()
    {
        var xml = new UnattendGenerator().Generate(State());
        Assert.Contains("MY-PC", xml);
        Assert.Contains("Contoso", xml);
        Assert.Contains("Admin", xml);
        Assert.Contains("VK7JG-NPHTM-C97JM-9MPGT-3V66T", xml);
    }

    [Fact]
    public void Generate_PasswordIsBase64Utf16()
    {
        var xml = new UnattendGenerator().Generate(State());
        var expected = UnattendGenerator.ToBase64("P@ssw0rd!");
        Assert.Contains(expected, xml);
    }

    [Fact]
    public void Generate_DiskConfigurationPresentWhenEnabled()
    {
        var xml = new UnattendGenerator().Generate(State());
        Assert.Contains("DiskConfiguration", xml);
        Assert.Contains("CreatePartition", xml);
    }

    [Fact]
    public void Generate_NoDiskConfigWhenDisabled()
    {
        var s = State();
        s.Unattend.ConfigureDisk = false;
        var xml = new UnattendGenerator().Generate(s);
        Assert.DoesNotContain("CreatePartition", xml);
    }
}

public class DependencyEngineTests
{
    [Fact]
    public void AutoLogonWithoutPassword_IsBlocker()
    {
        var state = new CustomizationState();
        state.Unattend.GenerateAutounattend = true;
        state.User.AutoLogin = true;
        state.User.Password = "";

        var issues = new DependencyEngine().Analyze(state);
        Assert.Contains(issues, i => i.Severity == IssueSeverity.Blocker && i.Code == "AUTOLOGON_NO_PASSWORD");
    }

    [Fact]
    public void StoreRemovedWithAppInstalls_IsBlocker()
    {
        var state = new CustomizationState();
        state.AppRemovals.Add(new AppRemovalEntry
        {
            PackageName = "Microsoft.WindowsStore_8wekyb3d8bbwe",
            Mode = AppRemovalMode.Remove
        });
        state.Applications.Add(new AppInstallEntry { Name = "7-Zip" });

        var issues = new DependencyEngine().Analyze(state);
        Assert.Contains(issues, i => i.Severity == IssueSeverity.Blocker && i.Code == "STORE_REMOVED_WITH_APPS");
    }

    [Fact]
    public void DisabledUpdateServiceWithUpdates_IsWarning()
    {
        var state = new CustomizationState();
        state.Updates.Add(new UpdateEntry { KbNumber = "KB123", Enabled = true });
        state.Services.Add(new ServiceEntry
        {
            ServiceName = "wuauserv", DisplayName = "Windows Update",
            Startup = StartupType.Disabled, Enabled = true
        });

        var issues = new DependencyEngine().Analyze(state);
        Assert.Contains(issues, i => i.Severity == IssueSeverity.Warning && i.Code == "UPDATE_SERVICE_DISABLED");
    }

    [Fact]
    public void MissingWallpaper_IsBlocker()
    {
        var state = new CustomizationState();
        state.Branding.WallpaperPath = "/definitely/not/here.jpg";
        var issues = new DependencyEngine().Analyze(state);
        Assert.Contains(issues, i => i.Code == "WALLPAPER_MISSING");
    }

    [Fact]
    public void DiskRepartition_RaisesCriticalRisk()
    {
        var state = new CustomizationState();
        state.Unattend.ConfigureDisk = true;
        Assert.Equal(RiskLevel.Critical, new DependencyEngine().MaxRisk(state));
    }

    [Fact]
    public void CleanState_CanBuild()
    {
        var state = new CustomizationState();
        state.Unattend.GenerateAutounattend = true;
        state.User.DefaultUsername = "User";
        state.User.Password = "x";
        Assert.True(new DependencyEngine().CanBuild(state, out var blockers));
        Assert.Empty(blockers);
    }
}

public class PresetLibraryTests : IDisposable
{
    private readonly string _root;
    private readonly PresetLibrary _library;
    private readonly Core.Logging.FileLogger _log;

    public PresetLibraryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "wc-preset-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _log = new Core.Logging.FileLogger();
        _library = new PresetLibrary(new Core.Utilities.AppPaths(_root), _log);
    }

    [Fact]
    public void BuiltIns_IncludePrivacyPerformanceSecurity()
    {
        var all = _library.GetAll();
        Assert.Contains(all, p => p.Category == "Privacy");
        Assert.Contains(all, p => p.Category == "Performance");
        Assert.Contains(all, p => p.Category == "Security");
        Assert.Contains(all, p => p.Category == "Windows UI");
    }

    [Fact]
    public void Apply_AddsTweaksAndTracksPreset()
    {
        var state = new CustomizationState();
        _library.Apply(state, "ui-taskbar-clean");
        Assert.True(state.RegistryTweaks.Count >= 5);
        Assert.Contains("ui-taskbar-clean", state.AppliedPresets);
    }

    [Fact]
    public void Remove_RemovesPresetTweaks()
    {
        var state = new CustomizationState();
        _library.Apply(state, "ui-taskbar-clean");
        _library.Remove(state, "ui-taskbar-clean");
        Assert.Empty(state.RegistryTweaks);
        Assert.Empty(state.AppliedPresets);
    }

    [Fact]
    public void Apply_IsIdempotent()
    {
        var state = new CustomizationState();
        _library.Apply(state, "ui-taskbar-clean");
        var count = state.RegistryTweaks.Count;
        _library.Apply(state, "ui-taskbar-clean");
        Assert.Equal(count, state.RegistryTweaks.Count);
    }

    [Fact]
    public void ExportImport_TemplateRoundTrips()
    {
        var state = new CustomizationState();
        _library.Apply(state, "security-enterprise");
        var path = Path.Combine(_root, "my-template.preset.json");
        _library.ExportTemplate(state, path);
        var imported = _library.ImportTemplate(path);
        Assert.Equal("Template", imported.Category);
    }

    public void Dispose()
    {
        _log.Dispose();
        try { Directory.Delete(_root, true); } catch { }
    }
}

public class ProcessRunnerTests
{
    private static bool IsWindows => OperatingSystem.IsWindows();

    [Fact]
    public async Task Run_CapturesOutputAndExitCode()
    {
        var log = new Core.Logging.FileLogger();
        var runner = new Core.Commands.ProcessRunner(log);
        var exe = IsWindows ? "cmd.exe" : "/bin/echo";
        var args = IsWindows ? new[] { "/c", "echo hello-wc" } : new[] { "hello-wc" };

        var result = await runner.RunAsync(new Core.Commands.CommandRequest(exe, args, Timeout: TimeSpan.FromSeconds(30)));
        Assert.True(result.Success);
        Assert.Contains("hello-wc", result.StdOut);
        Assert.Equal(0, result.ExitCode);
        log.Dispose();
    }

    [Fact]
    public async Task Run_NonZeroExit_IsNotSuccess()
    {
        var log = new Core.Logging.FileLogger();
        var runner = new Core.Commands.ProcessRunner(log);
        var exe = IsWindows ? "cmd.exe" : "/bin/sh";
        var args = IsWindows ? new[] { "/c", "exit 3" } : new[] { "-c", "exit 3" };

        var result = await runner.RunAsync(new Core.Commands.CommandRequest(exe, args, Timeout: TimeSpan.FromSeconds(30)));
        Assert.False(result.Success);
        Assert.Equal(3, result.ExitCode);
        log.Dispose();
    }

    [Fact]
    public async Task Run_Timeout_KillsProcess()
    {
        var log = new Core.Logging.FileLogger();
        var runner = new Core.Commands.ProcessRunner(log);
        var exe = IsWindows ? "cmd.exe" : "/bin/sh";
        var args = IsWindows ? new[] { "/c", "ping -n 30 127.0.0.1 > nul" } : new[] { "-c", "sleep 30" };

        var result = await runner.RunAsync(new Core.Commands.CommandRequest(exe, args, Timeout: TimeSpan.FromMilliseconds(500)));
        Assert.True(result.TimedOut);
        log.Dispose();
    }
}

public class ChangeNotificationTests
{
    [Fact]
    public void Entries_NotifyOnChange()
    {
        var app = new AppRemovalEntry { DisplayName = "X" };
        string? changed = null;
        app.PropertyChanged += (_, e) => changed = e.PropertyName;
        app.Mode = AppRemovalMode.Remove;
        Assert.Equal("Mode", changed);

        var svc = new ServiceEntry { ServiceName = "s", IsCritical = true };
        changed = null;
        svc.PropertyChanged += (_, e) => changed = e.PropertyName;
        svc.Startup = StartupType.Disabled;
        Assert.Equal("Startup", changed);

        var feat = new FeatureEntry();
        changed = null;
        feat.PropertyChanged += (_, e) => changed = e.PropertyName;
        feat.Enabled = false;
        Assert.Equal("Enabled", changed);
    }

    [Fact]
    public void Entries_NoNotificationWhenValueUnchanged()
    {
        var app = new AppRemovalEntry { Mode = AppRemovalMode.Keep };
        var fired = false;
        app.PropertyChanged += (_, _) => fired = true;
        app.Mode = AppRemovalMode.Keep;
        Assert.False(fired);
    }

    [Fact]
    public void ProjectModel_SerializesCustomization()
    {
        var p = new ProjectModel { Name = "T", WorkspacePath = Path.GetTempPath() };
        p.Customization.User.DefaultUsername = "Admin";
        p.Customization.RegistryTweaks.Add(new RegistryTweakEntry { KeyPath = @"SOFTWARE\T", ValueName = "V" });
        p.Source.ImageCount = 4;
        var json = System.Text.Json.JsonSerializer.Serialize(p);
        var back = System.Text.Json.JsonSerializer.Deserialize<ProjectModel>(json)!;
        Assert.Equal("Admin", back.Customization.User.DefaultUsername);
        Assert.Single(back.Customization.RegistryTweaks);
        Assert.Equal(4, back.Source.ImageCount);
    }
}

public class ServiceLifecycleTests
{
    [Fact]
    public void ServiceCreateTweaks_EmitsFullServiceKey()
    {
        var svc = new ServiceEntry
        {
            ServiceName = "MySvc", DisplayName = "My Service",
            Action = ServiceAction.Create, Startup = StartupType.Automatic,
            SourcePath = @"C:\host\svc.exe", ImagePath = @"C:\Program Files\MySvc\svc.exe",
            Enabled = true
        };
        var tweaks = RegFileBuilder.ServiceCreateTweaks(new[] { svc }).ToList();

        Assert.Equal(6, tweaks.Count);
        Assert.Contains(tweaks, t => t.ValueName == "Type" && t.Data == "16");
        Assert.Contains(tweaks, t => t.ValueName == "Start" && t.Data == "2");
        Assert.Contains(tweaks, t => t.ValueName == "ImagePath" && t.Data == @"C:\Program Files\MySvc\svc.exe");
        Assert.Contains(tweaks, t => t.ValueName == "ObjectName" && t.Data == "LocalSystem");
        Assert.All(tweaks, t => Assert.Equal(@"SYSTEM\CurrentControlSet\Services\MySvc", t.KeyPath));
    }

    [Fact]
    public void ServiceStartTweaks_SkipsCreateAndDelete()
    {
        var entries = new[]
        {
            new ServiceEntry { ServiceName = "A", Action = ServiceAction.Create, Enabled = true },
            new ServiceEntry { ServiceName = "B", Action = ServiceAction.Delete, Enabled = true },
            new ServiceEntry { ServiceName = "C", Action = ServiceAction.Configure, Enabled = true, Startup = StartupType.Disabled }
        };
        var tweaks = RegFileBuilder.ServiceStartTweaks(entries).ToList();
        Assert.Single(tweaks);
        Assert.Equal("4", tweaks[0].Data);
    }

    [Fact]
    public void ServiceDeletesReg_EmitsKeyDeletionDirectives()
    {
        var entries = new[]
        {
            new ServiceEntry { ServiceName = "Fax", Action = ServiceAction.Delete, Enabled = true },
            new ServiceEntry { ServiceName = "Keep", Action = ServiceAction.Configure, Enabled = true }
        };
        var reg = RegFileBuilder.ServiceDeletesReg(entries);

        Assert.StartsWith("Windows Registry Editor Version 5.00", reg);
        // .reg format escapes backslashes (doubled) — e.g. Services\\Fax
        Assert.Contains("[-HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Services\\Fax]", reg.Replace("\\\\", "\\"));
        Assert.DoesNotContain("Services\\Keep]", reg);
    }

    [Fact]
    public void ServiceDeletesReg_EmptyWhenNothingToDelete()
    {
        Assert.Equal(string.Empty, RegFileBuilder.ServiceDeletesReg(new[]
        {
            new ServiceEntry { ServiceName = "X", Action = ServiceAction.Configure, Enabled = true }
        }));
    }
}

public class WellKnownCatalogTests
{
    [Fact]
    public void Catalogs_ArePopulatedAndDocumented()
    {
        Assert.NotEmpty(WellKnownCatalog.ProvisionedApps);
        Assert.NotEmpty(WellKnownCatalog.Features);
        Assert.NotEmpty(WellKnownCatalog.Services);
        Assert.NotEmpty(WellKnownCatalog.Languages);
        Assert.NotEmpty(WellKnownCatalog.TimeZones);
        Assert.NotEmpty(WellKnownCatalog.KeyboardLayouts);
        Assert.NotEmpty(WellKnownCatalog.Regions);
        Assert.NotEmpty(WellKnownCatalog.ScriptSamples);

        Assert.All(WellKnownCatalog.ProvisionedApps, a => Assert.False(string.IsNullOrWhiteSpace(a.Description)));
        Assert.All(WellKnownCatalog.Features, f => Assert.False(string.IsNullOrWhiteSpace(f.Description)));
        Assert.All(WellKnownCatalog.Services, s => Assert.False(string.IsNullOrWhiteSpace(s.Description)));
    }

    [Fact]
    public void ScriptSamples_HaveCodeAndNames()
    {
        foreach (var sample in WellKnownCatalog.ScriptSamples)
        {
            Assert.False(string.IsNullOrWhiteSpace(sample.FileName));
            Assert.False(string.IsNullOrWhiteSpace(sample.Code));
            Assert.Contains('.', sample.FileName); // has an extension
        }
    }

    [Fact]
    public void ExpandedPresetCatalog_HasDocumentedPresets()
    {
        var presets = PresetCatalog.CreateBuiltIns();
        Assert.True(presets.Count >= 15, $"expected >= 15 built-in presets, got {presets.Count}");
        Assert.All(presets, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Description), $"preset {p.Id} lacks a description");
            Assert.NotEmpty(p.Tweaks.Concat<ChangeEntryBase>(p.ServiceChanges));
        });

        // Every tweak must state what it does.
        foreach (var tweak in presets.SelectMany(p => p.Tweaks))
        {
            Assert.False(string.IsNullOrWhiteSpace(tweak.Description), "tweak without description in " + tweak.Category);
        }
    }
}

public class KeyboardAndLocaleTests
{
    [Fact]
    public void BuildInputLocale_JoinsLayouts_FirstIsDefault()
    {
        var user = new UserSettings
        {
            KeyboardLayouts = new List<string> { "0409:00000409", "0429:00000429" }
        };
        Assert.Equal("0409:00000409;0429:00000429", UnattendGenerator.BuildInputLocale(user, "x"));
    }

    [Fact]
    public void BuildInputLocale_FallsBackToSingleThenFallback()
    {
        var user = new UserSettings { KeyboardLayout = "0407:00000407", KeyboardLayouts = new List<string>() };
        Assert.Equal("0407:00000407", UnattendGenerator.BuildInputLocale(user, "x"));

        var empty = new UserSettings { KeyboardLayout = "", KeyboardLayouts = new List<string>() };
        Assert.Equal("fa-IR", UnattendGenerator.BuildInputLocale(empty, "fa-IR"));
    }

    [Fact]
    public void Generate_UsesUserPageValues_ForLanguageTimezoneKeyboard()
    {
        var state = new CustomizationState();
        state.User.DefaultLanguage = "fa-IR";
        state.User.Region = "fa-IR";
        state.User.TimeZone = "Iran Standard Time";
        state.User.KeyboardLayouts = new List<string> { "0409:00000409", "0429:00000429" };
        // Language page left at defaults — the Users page must win.

        var xml = new UnattendGenerator().Generate(state);

        Assert.Contains("fa-IR", xml);
        Assert.Contains("Iran Standard Time", xml);
        Assert.Contains("0409:00000409;0429:00000429", xml);
        Assert.DoesNotContain("<UILanguage>en-US", xml.Replace("SetupUILanguage", "X")); // en-US must not be the UI language
    }

    [Fact]
    public void KeyboardLayoutOptions_AreNamedAndCoverEnglishPersian()
    {
        var options = WellKnownCatalog.KeyboardLayouts;
        Assert.NotEmpty(options);
        Assert.All(options, o =>
        {
            Assert.False(string.IsNullOrWhiteSpace(o.Name));
            Assert.Contains(':', o.Code);
        });
        Assert.Equal("English (United States)", WellKnownCatalog.KeyboardLayoutName("0409:00000409"));
        Assert.Contains(options, o => o.Code == "0429:00000429"); // Persian present
    }

    [Fact]
    public void ScriptSamples_HaveEnoughVariety()
    {
        Assert.True(WellKnownCatalog.ScriptSamples.Count >= 20,
            $"expected >= 20 samples, got {WellKnownCatalog.ScriptSamples.Count}");
        Assert.Contains(WellKnownCatalog.ScriptSamples, s => s.Kind == "Registry");
        Assert.Contains(WellKnownCatalog.ScriptSamples, s => s.Kind == "Cmd");
        Assert.Contains(WellKnownCatalog.ScriptSamples, s => s.Kind == "PowerShell");
    }
}
