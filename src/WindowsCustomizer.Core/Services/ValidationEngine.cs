using WindowsCustomizer.Core.Logging;
using WindowsCustomizer.Core.Models;

namespace WindowsCustomizer.Core.Services;

// =================================================================================================
// Validation Engine (spec 34 + project pre-checks). Pure logic + ISO inspection reuse.
// =================================================================================================

public sealed record ValidationIssue(IssueSeverity Severity, string Code, string Message, string Fix);

public sealed class ValidationEngine
{
    private readonly IAppLogger _log;

    public ValidationEngine(IAppLogger log)
    {
        _log = log;
    }

    /// <summary>Pre-build project validation.</summary>
    public IReadOnlyList<ValidationIssue> ValidateProject(ProjectModel project)
    {
        var issues = new List<ValidationIssue>();
        var state = project.Customization;

        if (!File.Exists(project.Source.OriginalIsoPath))
        {
            issues.Add(new ValidationIssue(IssueSeverity.Blocker, "SRC_MISSING",
                $"Source ISO not found: {project.Source.OriginalIsoPath}",
                "Re-select the ISO on the Source page."));
        }

        if (state.SelectedImageIndexes.Count == 0)
        {
            issues.Add(new ValidationIssue(IssueSeverity.Warning, "NO_INDEX",
                "No edition index selected — index 1 will be used.",
                "Select the target edition on the Edition page."));
        }

        foreach (var d in state.Drivers.Where(d => d.Enabled && !File.Exists(d.SourcePath) && !Directory.Exists(d.SourcePath)))
        {
            issues.Add(new ValidationIssue(IssueSeverity.Blocker, "DRV_MISSING",
                $"Driver source not found: {d.SourcePath}", "Remove or re-add the driver."));
        }

        foreach (var u in state.Updates.Where(u => u.Enabled && !File.Exists(u.SourcePath)))
        {
            issues.Add(new ValidationIssue(IssueSeverity.Blocker, "UPD_MISSING",
                $"Update file not found: {u.SourcePath}", "Remove or re-add the update."));
        }

        foreach (var s in state.Scripts.Where(s => s.Enabled && !File.Exists(s.SourcePath)))
        {
            issues.Add(new ValidationIssue(IssueSeverity.Blocker, "SCR_MISSING",
                $"Script file not found: {s.SourcePath}", "Remove or re-add the script."));
        }

        foreach (var a in state.Applications.Where(a => a.Enabled && !File.Exists(a.InstallerPath)))
        {
            issues.Add(new ValidationIssue(IssueSeverity.Blocker, "APP_MISSING",
                $"Installer not found: {a.InstallerPath}", "Remove or re-add the application."));
        }

        foreach (var f in state.FileCopies.Where(f => f.Enabled && !File.Exists(f.SourcePath) && !Directory.Exists(f.SourcePath)))
        {
            issues.Add(new ValidationIssue(IssueSeverity.Blocker, "FILE_MISSING",
                $"Custom file source not found: {f.SourcePath}", "Remove or re-add the file entry."));
        }

        if (!state.Unattend.GenerateAutounattend)
        {
            issues.Add(new ValidationIssue(IssueSeverity.Info, "NO_UNATTEND",
                "Autounattend generation is disabled — setup will use interactive defaults.", ""));
        }

        return issues;
    }

    /// <summary>Post-build output ISO validation (spec 34).</summary>
    public async Task<IReadOnlyList<ValidationIssue>> ValidateOutputIsoAsync(string isoPath, CancellationToken ct = default)
    {
        var issues = new List<ValidationIssue>();

        if (!File.Exists(isoPath))
        {
            issues.Add(new ValidationIssue(IssueSeverity.Blocker, "ISO_MISSING", "Output ISO does not exist.", "Rebuild."));
            return issues;
        }

        var analyzer = new IsoAnalyzer(_log);
        var result = await analyzer.AnalyzeAsync(isoPath, null, ct).ConfigureAwait(false);

        if (!result.Success)
        {
            foreach (var e in result.Errors)
            {
                issues.Add(new ValidationIssue(IssueSeverity.Blocker, "ISO_ANALYZE", e, "Rebuild the ISO."));
            }
        }

        // Boot files
        if (!result.BootFiles.Any(b => b.Kind == BootFileKind.BiosBootManager || b.Kind == BootFileKind.EfiX64))
        {
            issues.Add(new ValidationIssue(IssueSeverity.Blocker, "NO_BOOT",
                "The output ISO contains no BIOS/UEFI boot files.", "Rebuild with oscdimg (Windows ADK) for full boot support."));
        }

        // Install payload
        if (result.InstallImageFormat == InstallImageFormat.None)
        {
            issues.Add(new ValidationIssue(IssueSeverity.Blocker, "NO_PAYLOAD",
                "No install.wim found inside the output ISO.", "Rebuild."));
        }
        else if (result.InstallImageHeader is null)
        {
            issues.Add(new ValidationIssue(IssueSeverity.Warning, "BAD_HEADER",
                "The install image header could not be parsed.", "Verify the WIM was copied correctly."));
        }

        // Autounattend
        var hasUnattend = result.SourcesFiles.Concat(result.RootEntries)
            .Any(e => e.Name.Contains("Autounattend", StringComparison.OrdinalIgnoreCase));
        if (!hasUnattend)
        {
            issues.Add(new ValidationIssue(IssueSeverity.Info, "NO_AUTOUNATTEND",
                "No Autounattend.xml at the ISO root (setup will be interactive).", ""));
        }

        // Structure
        if (result.RootEntries.Count < 3)
        {
            issues.Add(new ValidationIssue(IssueSeverity.Warning, "THIN_STRUCTURE",
                "The ISO structure looks incomplete.", "Check the build log for extraction errors."));
        }

        return issues;
    }
}
