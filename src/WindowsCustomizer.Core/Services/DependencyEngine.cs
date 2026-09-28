using WindowsCustomizer.Core.Models;

namespace WindowsCustomizer.Core.Services;

// =================================================================================================
// Dependency / conflict engine (spec 28) + Safety system (spec 29). Rules are declarative and run
// on every Review + before every Build: blockers stop the build, warnings need confirmation.
// =================================================================================================

public enum IssueSeverity { Info = 0, Warning = 1, Blocker = 2 }

public sealed record DependencyIssue(
    IssueSeverity Severity,
    string Code,
    string Message,
    string Resolution);

public sealed class DependencyEngine
{
    public IReadOnlyList<DependencyIssue> Analyze(CustomizationState state)
    {
        var issues = new List<DependencyIssue>();

        // --- user / setup ---
        if (state.Unattend.GenerateAutounattend && state.User.AutoLogin && !state.User.HasPassword)
        {
            issues.Add(new DependencyIssue(IssueSeverity.Blocker, "AUTOLOGON_NO_PASSWORD",
                "Auto-login is enabled but no password is set.",
                "Set a password for the default user, or disable Auto Login."));
        }

        if (state.Unattend.GenerateAutounattend && string.IsNullOrWhiteSpace(state.User.DefaultUsername))
        {
            issues.Add(new DependencyIssue(IssueSeverity.Blocker, "NO_USERNAME",
                "Autounattend is enabled but no default user name is defined.",
                "Set a user name on the Users page."));
        }

        if (state.SelectionMode == "Multiple" && state.SelectedImageIndexes.Count == 0)
        {
            issues.Add(new DependencyIssue(IssueSeverity.Blocker, "NO_EDITION",
                "Multiple-image mode is selected but no editions are chosen.",
                "Pick at least one edition on the Edition page."));
        }

        // --- apps vs store ---
        if (state.AppRemovals.Any(a => a.Mode == AppRemovalMode.Remove &&
                                       a.PackageName.Contains("WindowsStore", StringComparison.OrdinalIgnoreCase)) &&
            state.Applications.Count > 0)
        {
            issues.Add(new DependencyIssue(IssueSeverity.Blocker, "STORE_REMOVED_WITH_APPS",
                "Microsoft Store is set to be removed but automatic application installs are configured.",
                "Keep the Store package, or remove the application installs."));
        }

        // --- updates vs service ---
        if (state.Updates.Any(u => u.Enabled) &&
            state.Services.Any(s => s.Enabled && s.ServiceName.Equals("wuauserv", StringComparison.OrdinalIgnoreCase) &&
                                    s.Startup == StartupType.Disabled))
        {
            issues.Add(new DependencyIssue(IssueSeverity.Warning, "UPDATE_SERVICE_DISABLED",
                "Windows Update service is disabled but updates are being added to the image.",
                "Servicing still works offline, but the installed OS will not check for updates. Re-enable wuauserv if that is not intended."));
        }

        // --- sensitive app removals ---
        foreach (var app in state.AppRemovals.Where(a => a.Mode == AppRemovalMode.Remove && a.IsSensitive))
        {
            issues.Add(new DependencyIssue(IssueSeverity.Warning, "SENSITIVE_APP",
                $"'{app.DisplayName}' is a system-related package — removing it may break Setup, Shell or Store features.",
                "Verify you understand the impact, or set it to Keep/Optional."));
        }

        // --- critical services ---
        foreach (var svc in state.Services.Where(s => s.Enabled && s.IsCritical && s.Startup == StartupType.Disabled))
        {
            issues.Add(new DependencyIssue(IssueSeverity.Warning, "CRITICAL_SERVICE_DISABLED",
                $"'{svc.DisplayName}' is critical and is set to Disabled. {svc.Warning}".Trim(),
                "Change it to Manual, or accept the risk explicitly in the Review page."));
        }

        // --- scripts security ---
        foreach (var script in state.Scripts.Where(s => s.Enabled && !s.ExternalWarningAccepted))
        {
            issues.Add(new DependencyIssue(IssueSeverity.Warning, "EXTERNAL_SCRIPT",
                $"Script '{script.FileName}' is an external file and has not been acknowledged.",
                "Open the Scripts page and confirm you trust this file (its SHA-256 is recorded)."));
        }

        // --- branding wallpaper path ---
        if (!string.IsNullOrWhiteSpace(state.Branding.WallpaperPath) && !File.Exists(state.Branding.WallpaperPath))
        {
            issues.Add(new DependencyIssue(IssueSeverity.Blocker, "WALLPAPER_MISSING",
                $"Wallpaper file not found: {state.Branding.WallpaperPath}",
                "Pick an existing image file on the Branding page."));
        }

        // --- risky registry combination: UAC off ---
        if (state.RegistryTweaks.Any(t => t.Enabled && t.ValueName == "EnableLUA" && t.Data == "0"))
        {
            issues.Add(new DependencyIssue(IssueSeverity.Warning, "UAC_DISABLED",
                "User Account Control is being disabled (EnableLUA=0).",
                "This weakens system security significantly — only do this for controlled/kiosk images."));
        }

        return issues;
    }

    public bool CanBuild(CustomizationState state, out IReadOnlyList<DependencyIssue> blockers)
    {
        blockers = Analyze(state).Where(i => i.Severity == IssueSeverity.Blocker).ToList();
        return blockers.Count == 0;
    }

    /// <summary>Maximum risk across all enabled changes (drives the Safety confirmations).</summary>
    public RiskLevel MaxRisk(CustomizationState state)
    {
        var max = RiskLevel.Safe;
        void Consider(RiskLevel r)
        {
            if (r > max)
            {
                max = r;
            }
        }

        foreach (var t in state.RegistryTweaks.Where(x => x.Enabled))
        {
            Consider(t.Risk);
        }

        foreach (var s in state.Services.Where(x => x.Enabled))
        {
            Consider(s.IsCritical && s.Startup == StartupType.Disabled ? RiskLevel.Critical : s.Risk);
        }

        foreach (var a in state.AppRemovals.Where(x => x.Mode == AppRemovalMode.Remove))
        {
            Consider(a.IsSensitive ? RiskLevel.High : RiskLevel.Medium);
        }

        foreach (var f in state.Features.Where(x => x.Enabled))
        {
            Consider(f.Remove ? RiskLevel.Medium : RiskLevel.Low);
        }

        if (state.Unattend.ConfigureDisk)
        {
            Consider(RiskLevel.Critical); // disk repartition wipes the target machine
        }

        return max;
    }
}
