using System.Diagnostics;
using System.Text;
using System.Text.Json;
using WindowsCustomizer.Core.Commands;
using WindowsCustomizer.Core.Engines;
using WindowsCustomizer.Core.Logging;
using WindowsCustomizer.Core.Models;
using WindowsCustomizer.Core.Utilities;

namespace WindowsCustomizer.Core.Services;

// =================================================================================================
// Central Build Engine (spec 32/50). Executes the full pipeline:
// Validate → Prepare → ESD→WIM → Mount → Drivers → Updates → Features → Registry → Services →
// App Removal → Files → Apps payload → Scripts → Autounattend → Cleanup → Commit → Optimize →
// ISO content → Bootable ISO → Validate → Report.
// Every step reports progress, is cancellable at safe points, and everything lands in the report.
// =================================================================================================

public sealed record BuildStepResult(string Name, bool Success, string Detail, TimeSpan Duration);

public sealed class BuildProgress
{
    public string Step { get; set; } = string.Empty;
    public double Percent { get; set; }
    public string Detail { get; set; } = string.Empty;

    /// <summary>false = the step just started; true = the step finished (see <see cref="Success"/>).</summary>
    public bool Finished { get; set; }

    public bool Success { get; set; }
}

public sealed class BuildReport
{
    public string ProjectName { get; set; } = string.Empty;
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset FinishedUtc { get; set; }
    public bool Success { get; set; }
    public string OutputIso { get; set; } = string.Empty;
    public string Strategy { get; set; } = string.Empty;
    public List<BuildStepResult> Steps { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public List<string> ArtifactHashes { get; set; } = new();
}

public sealed class BuildPipeline
{
    private readonly DismEngine _dism;
    private readonly RegistryEngine _registry;
    private readonly IsoBuilder _isoBuilder;
    private readonly IProcessRunner _runner;
    private readonly ValidationEngine _validation;
    private readonly IAppLogger _log;

    public BuildPipeline(
        DismEngine dism,
        RegistryEngine registry,
        IsoBuilder isoBuilder,
        IProcessRunner runner,
        ValidationEngine validation,
        IAppLogger log)
    {
        _dism = dism;
        _registry = registry;
        _isoBuilder = isoBuilder;
        _runner = runner;
        _validation = validation;
        _log = log;
    }

    /// <summary>Estimates free space required on the workspace drive (spec 43).</summary>
    public static (long SourceBytes, long WorkBytes, long TempBytes, long OutputBytes, long RequiredBytes) EstimateSpace(ProjectModel project)
    {
        var source = project.Source.SizeBytes;
        var work = (long)(source * 1.6);   // extracted image + wim
        var temp = (long)(source * 1.2);   // iso content copy
        var output = (long)(source * 1.1); // final iso
        return (source, work, temp, output, source + work + temp + output);
    }

    public async Task<BuildReport> BuildAsync(
        ProjectModel project,
        IProgress<BuildProgress>? progress = null,
        CancellationToken ct = default)
    {
        var state = project.Customization;
        var report = new BuildReport
        {
            ProjectName = project.Name,
            StartedUtc = DateTimeOffset.UtcNow
        };

        var ws = project.WorkspacePath;
        // DISM /Mount-Image needs reparse points: the mount point MUST live on NTFS.
        // (e.g. an exFAT project drive fails with 0xc142011f — so we relocate automatically.)
        var mountDir = ResolveNtfsMountDir(Path.Combine(ws, "Mount", "image"), report);
        var imagesDir = Path.Combine(ws, "Images");
        var tempDir = Path.Combine(ws, "Temp", "iso-content");
        var outputDir = Path.Combine(ws, "Output");
        var configDir = Path.Combine(ws, "Config");
        var scriptDir = Path.Combine(ws, "Scripts");

        var steps = new List<(string Name, Func<CancellationToken, Task<string>> Run)>();
                // In-step progress is log-only — the runner loop reports step start/finish to the UI
        // (sending a second progress event here would duplicate the live step rows).
        void Report(string step, string detail = "")
        {
            _log.Info("Build", $"STEP: {step} {detail}".Trim());
        }

        // ---------------------------------------------------------------- 1. validate
        steps.Add(("Validate Project", async ct2 =>
        {
            Report("Validate Project");
            if (!DismEngine.IsWindowsHost)
            {
                throw new InvalidOperationException("Image servicing requires Windows (DISM). Run the build on a Windows machine.");
            }

            var issues = _validation.ValidateProject(project);
            var blockers = issues.Where(i => i.Severity == IssueSeverity.Blocker).ToList();
            if (blockers.Count > 0)
            {
                throw new InvalidOperationException("Project validation failed:\n" +
                                                    string.Join("\n", blockers.Select(b => b.Message)));
            }

            foreach (var w in issues.Where(i => i.Severity == IssueSeverity.Warning))
            {
                report.Warnings.Add(w.Message);
            }

            return $"Validation passed ({issues.Count} note(s)).";
        }));

        // ---------------------------------------------------------------- 2. prepare workspace
        steps.Add(("Prepare Workspace", async ct2 =>
        {
            Report("Prepare Workspace");
            Directory.CreateDirectory(mountDir);
            Directory.CreateDirectory(imagesDir);
            Directory.CreateDirectory(outputDir);
            Directory.CreateDirectory(configDir);

            var (s, work, tmp, outp, required) = EstimateSpace(project);
            var drive = new DriveInfo(Path.GetPathRoot(ws)!);
            if (drive.AvailableFreeSpace < required)
            {
                throw new IOException($"Not enough free space. Required ~{required / 1_000_000_000.0:0.0} GB, " +
                                      $"available {drive.AvailableFreeSpace / 1_000_000_000.0:0.0} GB.");
            }

            return $"Workspace ready (need ~{required / 1_000_000_000.0:0.0} GB).";
        }));

        // ---------------------------------------------------------------- 3. working image
        var workingWim = Path.Combine(imagesDir, "install.wim");
        steps.Add(("Prepare Working Image", async ct2 =>
        {
            Report("Prepare Working Image");
            var progress = new Progress<string>(m => _log.Info("Build", m));

            // Reuses Images\install.wim|esd when already extracted (e.g. by the Edition page) —
            // otherwise extracts it from the ISO with visible progress.
            var srcWim = await Task.Run(() => InstallPayloadExtractor.Ensure(project, _log, progress), ct2).ConfigureAwait(false);

            if (srcWim.EndsWith(".esd", StringComparison.OrdinalIgnoreCase))
            {
                report.Warnings.Add("Source payload is install.esd — exporting to WIM (this takes a while).");
                var index = state.SelectedImageIndexes.FirstOrDefault() <= 0 ? 1 : state.SelectedImageIndexes[0];
                var result = await _dism.ConvertEsdToWimAsync(srcWim, workingWim, index, ct2).ConfigureAwait(false);
                if (!result.Success)
                {
                    throw new InvalidOperationException("ESD → WIM conversion failed.");
                }

                return "Converted install.esd → install.wim (compression: max).";
            }

            // The extracted payload IS the working copy — never copy a file onto itself.
            if (string.Equals(Path.GetFullPath(srcWim), Path.GetFullPath(workingWim), StringComparison.OrdinalIgnoreCase))
            {
                return $"Working copy ready: {workingWim}";
            }

            await using (var src = File.OpenRead(srcWim))
            {
                InstallPayloadExtractor.CopyWithProgress(src, workingWim, src.Length, "install.wim",
                    new Progress<string>(m => _log.Info("Build", m)));
            }

            return $"Working copy created: {workingWim}";
        }));

        // ---------------------------------------------------------------- 4. mount
        steps.Add(("Mount Image", async ct2 =>
        {
            Report("Mount Image");
            var index = state.SelectedImageIndexes.FirstOrDefault() <= 0 ? 1 : state.SelectedImageIndexes[0];

            // Leftovers from a crashed run make DISM fail ("the directory is not empty").
            if (Directory.Exists(mountDir) && Directory.EnumerateFileSystemEntries(mountDir).Any())
            {
                report.Warnings.Add("Mount directory was not empty (leftover of a previous run) — cleaned before mounting.");
                Directory.Delete(mountDir, recursive: true);
            }

            var result = await _dism.MountImageAsync(workingWim, index, mountDir, readOnly: false, ct2).ConfigureAwait(false);
            if (!result.Success)
            {
                // DISM often writes the real error to STDOUT — surface it so the report is actionable.
                var detail = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
                throw new InvalidOperationException($"Mount failed (exit {result.ExitCode}). {detail}".Trim());
            }

            return $"Mounted index {index} at {mountDir}";
        }));

        // ---------------------------------------------------------------- 5. drivers
        steps.Add(("Inject Drivers", async ct2 =>
        {
            Report("Inject Drivers");
            var drivers = state.Drivers.Where(d => d.Enabled).ToList();
            foreach (var d in drivers)
            {
                var r = await _dism.AddDriverAsync(mountDir, d.SourcePath, recurse: d.IsFolder, forceUnsigned: false, ct2).ConfigureAwait(false);
                if (!r.Success)
                {
                    report.Warnings.Add($"Driver '{d.DisplayName}' failed to inject (exit {r.ExitCode}).");
                }
            }

            return $"{drivers.Count} driver package(s) processed.";
        }));

        // ---------------------------------------------------------------- 6. updates
        steps.Add(("Install Updates", async ct2 =>
        {
            Report("Install Updates");
            var updates = state.Updates.Where(u => u.Enabled).ToList();
            foreach (var u in updates.OrderBy(x => x.UpdateType == "ServicingStack" ? 0 : 1))
            {
                var r = await _dism.AddPackageAsync(mountDir, u.SourcePath, ignoreCheck: false, ct2).ConfigureAwait(false);
                if (!r.Success)
                {
                    report.Warnings.Add($"Update '{u.KbNumber}' failed (exit {r.ExitCode}).");
                }
            }

            return $"{updates.Count} package(s) added (SSU first).";
        }));

        // ---------------------------------------------------------------- 7. features
        steps.Add(("Apply Features", async ct2 =>
        {
            Report("Apply Features");
            var count = 0;
            foreach (var f in state.Features.Where(f => f.Enabled))
            {
                if (f.Remove)
                {
                    var r = await _dism.RemovePackageAsync(mountDir, f.FeatureName, ct2).ConfigureAwait(false);
                    if (!r.Success)
                    {
                        report.Warnings.Add($"Feature/package '{f.FeatureName}' could not be removed.");
                    }
                }
                else
                {
                    await _dism.SetFeatureAsync(mountDir, f.FeatureName, f.Enable, ct2).ConfigureAwait(false);
                }

                count++;
            }

            return $"{count} feature change(s) applied.";
        }));

        // ---------------------------------------------------------------- 8. registry + services
        Func<CancellationToken, Task<string>> registryStep = async ct2 =>
        {
            Report("Apply Registry & Services");
            var allTweaks = state.RegistryTweaks
                .Concat(RegFileBuilder.ServiceStartTweaks(state.Services))
                .Concat(RegFileBuilder.ServiceCreateTweaks(state.Services))
                .ToList();
            await _registry.ApplyTweaksAsync(mountDir, allTweaks, Path.Combine(ws, "Temp", "reg"), ct2).ConfigureAwait(false);

            // Delete unwanted services via raw key deletion in the offline SYSTEM hive.
            var deletes = RegFileBuilder.ServiceDeletesReg(state.Services);
            if (!string.IsNullOrEmpty(deletes))
            {
                await _registry.ApplyRawRegAsync(mountDir, "WCS_SYSTEM", deletes, Path.Combine(ws, "Temp", "reg"), ct2).ConfigureAwait(false);
            }

            // Copy the binaries of created services into the image.
            foreach (var svc in state.Services.Where(s => s.Enabled && s.Action == ServiceAction.Create))
            {
                if (!string.IsNullOrWhiteSpace(svc.SourcePath) && !string.IsNullOrWhiteSpace(svc.ImagePath) && File.Exists(svc.SourcePath))
                {
                    var target = Path.Combine(mountDir, svc.ImagePath.TrimStart('C', ':', '\\', '/').Replace('/', Path.DirectorySeparatorChar));
                    PathGuard.EnsureInsideWorkspace(mountDir, target);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(svc.SourcePath, target, overwrite: true);
                }
            }

            var deletesCount = state.Services.Count(s => s.Enabled && s.Action == ServiceAction.Delete);
            var createsCount = state.Services.Count(s => s.Enabled && s.Action == ServiceAction.Create);
            return allTweaks.Count(t => t.Enabled) + " change(s) written to the offline registry" +
                   (createsCount > 0 ? $", {createsCount} service(s) created" : string.Empty) +
                   (deletesCount > 0 ? $", {deletesCount} service(s) deleted" : string.Empty) + ".";
        };
        steps.Add(("Apply Registry & Services", registryStep));

        // ---------------------------------------------------------------- 9. remove apps
        steps.Add(("Remove Provisioned Apps", async ct2 =>
        {
            Report("Remove Provisioned Apps");
            var removals = state.AppRemovals.Where(a => a.Mode == AppRemovalMode.Remove).ToList();
            foreach (var app in removals)
            {
                var r = await _dism.RemoveProvisionedAppAsync(mountDir, app.PackageName, ct2).ConfigureAwait(false);
                if (!r.Success)
                {
                    report.Warnings.Add($"App '{app.DisplayName}' could not be removed.");
                }
            }

            return $"{removals.Count} provisioned app(s) removed.";
        }));

        // ---------------------------------------------------------------- 10. files & app payload
        steps.Add(("Add Files & Applications", async ct2 =>
        {
            Report("Add Files & Applications");
            foreach (var f in state.FileCopies.Where(f => f.Enabled))
            {
                var target = Path.Combine(mountDir, f.TargetPath.TrimStart('C', ':', '\\', '/').Replace('/', Path.DirectorySeparatorChar));
                PathGuard.EnsureInsideWorkspace(mountDir, target);
                if (f.IsFolder)
                {
                    CopyDirectory(f.SourcePath, target);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(f.SourcePath, target, overwrite: true);
                }
            }

            // Application packages: AppX/MSIX are provisioned directly into the image; EXE/MSI
            // installers are staged and installed by FirstLogon.cmd at first boot.
            var apps = state.Applications.Where(a => a.Enabled).OrderBy(a => a.InstallOrder).ToList();
            var provisioned = 0;
            var staged = new List<AppInstallEntry>();
            foreach (var app in apps)
            {
                var ext = Path.GetExtension(app.InstallerPath).ToLowerInvariant();
                if (ext is ".appx" or ".msix" or ".appxbundle" or ".msixbundle")
                {
                    var r = await _dism.AddProvisionedAppxAsync(mountDir, app.InstallerPath, ct2).ConfigureAwait(false);
                    if (r.Success)
                    {
                        provisioned++;
                    }
                    else
                    {
                        report.Warnings.Add($"AppX package '{app.Name}' could not be provisioned (exit {r.ExitCode}).");
                    }
                }
                else
                {
                    staged.Add(app);
                }
            }

            if (staged.Count > 0)
            {
                var appRoot = Path.Combine(mountDir, "Windows", "Setup", "Scripts", "WC", "Apps");
                Directory.CreateDirectory(appRoot);
                foreach (var app in staged)
                {
                    File.Copy(app.InstallerPath, Path.Combine(appRoot, Path.GetFileName(app.InstallerPath)), overwrite: true);
                }

                var installScript = GenerateAppInstallScript(staged);
                await File.WriteAllTextAsync(Path.Combine(appRoot, "InstallApps.cmd"), installScript, ct2).ConfigureAwait(false);
            }

            return $"{state.FileCopies.Count(f => f.Enabled)} file set(s), {provisioned} AppX provisioned, {staged.Count} installer payload(s).";
        }));

        // ---------------------------------------------------------------- 11. scripts + unattend inside image
        steps.Add(("Apply Scripts & Setup Config", async ct2 =>
        {
            Report("Apply Scripts & Setup Config");
            var wc = Path.Combine(mountDir, "Windows", "Setup", "Scripts", "WC");
            Directory.CreateDirectory(wc);

            foreach (var s in state.Scripts.Where(s => s.Enabled))
            {
                File.Copy(s.SourcePath, Path.Combine(wc, s.FileName), overwrite: true);
            }

            var generated = ScriptEngine.GenerateScriptLaunchers(state.Scripts.Where(s => s.Enabled).ToList());
            foreach (var (name, content) in generated)
            {
                await File.WriteAllTextAsync(Path.Combine(wc, name), content, ct2).ConfigureAwait(false);
            }

            // unattend inside the image runs during specialize; Autounattend.xml at ISO root runs setup.
            if (state.Unattend.GenerateAutounattend)
            {
                var xml = new UnattendGenerator().Generate(state);
                await File.WriteAllTextAsync(Path.Combine(mountDir, "Windows", "System32", "Sysprep", "unattend.xml"), xml, ct2).ConfigureAwait(false);
                await File.WriteAllTextAsync(Path.Combine(configDir, "Autounattend.xml"), xml, ct2).ConfigureAwait(false);
            }

            // Branding
            if (!string.IsNullOrWhiteSpace(state.Branding.WallpaperPath) && File.Exists(state.Branding.WallpaperPath))
            {
                var wallDir = Path.Combine(mountDir, "Windows", "Web", "Wallpaper", "WC");
                Directory.CreateDirectory(wallDir);
                File.Copy(state.Branding.WallpaperPath, Path.Combine(wallDir, Path.GetFileName(state.Branding.WallpaperPath)), overwrite: true);
            }

            if (!string.IsNullOrWhiteSpace(state.Branding.Manufacturer))
            {
                var oem = GenerateOemInfo(state.Branding);
                await File.WriteAllTextAsync(Path.Combine(mountDir, "Windows", "System32", "oobe", "info", "info.ini"), oem, ct2).ConfigureAwait(false);
            }

            return $"{state.Scripts.Count(s => s.Enabled)} script(s), unattend + branding staged.";
        }));

        // ---------------------------------------------------------------- 12. cleanup + commit
        steps.Add(("Cleanup & Commit", async ct2 =>
        {
            Report("Cleanup & Commit");
            await _dism.CleanupImageAsync(mountDir, ct2).ConfigureAwait(false);
            var commit = await _dism.UnmountImageAsync(mountDir, commit: true, ct2).ConfigureAwait(false);
            if (!commit.Success)
            {
                throw new InvalidOperationException($"Commit failed (exit {commit.ExitCode}).");
            }

            return "Image committed.";
        }));

        // ---------------------------------------------------------------- 13. optimize
        steps.Add(("Optimize Image", async ct2 =>
        {
            Report("Optimize Image");
            var optimized = Path.Combine(imagesDir, "install-opt.wim");
            var index = state.SelectedImageIndexes.FirstOrDefault() <= 0 ? 1 : state.SelectedImageIndexes[0];
            var r = await _dism.ExportImageAsync(workingWim, index, optimized, "max", ct2).ConfigureAwait(false);
            if (r.Success && File.Exists(optimized))
            {
                File.Move(optimized, workingWim, overwrite: true);
                return "Exported with compression max + integrity check.";
            }

            report.Warnings.Add("Optimize step skipped (export failed) — keeping the working WIM.");
            return "Skipped.";
        }));

        // ---------------------------------------------------------------- 14. build ISO
        string isoStrategy = "";
        Func<CancellationToken, Task<string>> isoStep = async ct2 =>
        {
            Report("Create Bootable ISO");
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }

            await Task.Run(() => ExtractIsoContent(project.Source.OriginalIsoPath, tempDir), ct2).ConfigureAwait(false);

            // Replace the payload + drop Autounattend.xml at the root.
            foreach (var name in new[] { "install.wim", "install.esd", "install.swm" })
            {
                var p = Path.Combine(tempDir, "sources", name);
                if (File.Exists(p))
                {
                    File.Delete(p);
                }
            }

            File.Copy(workingWim, Path.Combine(tempDir, "sources", "install.wim"), overwrite: true);
            if (state.Unattend.GenerateAutounattend)
            {
                File.Copy(Path.Combine(configDir, "Autounattend.xml"), Path.Combine(tempDir, "Autounattend.xml"), overwrite: true);
            }

            var outputIso = Path.Combine(outputDir, PathGuard.SanitizeFileName(project.Settings.OutputFileName));
            var volumeLabel = string.IsNullOrWhiteSpace(project.Source.VolumeLabel)
                ? "WIN_CUSTOM"
                : project.Source.VolumeLabel;

            var build = await _isoBuilder.BuildAsync(tempDir, outputIso, volumeLabel, ct2).ConfigureAwait(false);
            isoStrategy = build.Strategy;
            report.OutputIso = build.OutputPath;
            report.Strategy = build.Strategy;
            foreach (var w in build.Warnings)
            {
                report.Warnings.Add(w);
            }

            if (!build.Success)
            {
                throw new InvalidOperationException("ISO creation failed.");
            }

            var hash = await HashUtil.ComputeSha256Async(build.OutputPath, null, ct2).ConfigureAwait(false);
            report.ArtifactHashes.Add("ISO SHA-256: " + hash);
            return "ISO created via " + build.Strategy + " (" + build.SizeBytes / 1_000_000.0 + " MB).";
        };
        steps.Add(("Create Bootable ISO", isoStep));

        // ---------------------------------------------------------------- 15. validate output
        steps.Add(("Validate ISO", async ct2 =>
        {
            Report("Validate ISO");
            var result = await _validation.ValidateOutputIsoAsync(report.OutputIso, ct2).ConfigureAwait(false);
            var blockers = result.Where(i => i.Severity == IssueSeverity.Blocker).ToList();
            foreach (var w in result.Where(i => i.Severity != IssueSeverity.Blocker))
            {
                report.Warnings.Add(w.Message);
            }

            if (blockers.Count > 0)
            {
                throw new InvalidOperationException("Output validation failed:\n" +
                                                    string.Join("\n", blockers.Select(b => b.Message)));
            }

            return $"Output ISO validated ({result.Count} checks).";
        }));

        // ---------------------------------------------------------------- run all
        var stopwatch = Stopwatch.StartNew();
        try
        {
            foreach (var (name, run) in steps)
            {
                ct.ThrowIfCancellationRequested();
                var stepWatch = Stopwatch.StartNew();
                progress?.Report(new BuildProgress { Step = name, Finished = false, Detail = "In progress…" });
                try
                {
                    var detail = await run(ct).ConfigureAwait(false);
                    stepWatch.Stop();
                    report.Steps.Add(new BuildStepResult(name, true, detail, stepWatch.Elapsed));
                    progress?.Report(new BuildProgress { Step = name, Finished = true, Success = true, Percent = 100, Detail = detail });
                }
                catch (OperationCanceledException)
                {
                    stepWatch.Stop();
                    report.Steps.Add(new BuildStepResult(name, false, "Cancelled", stepWatch.Elapsed));
                    progress?.Report(new BuildProgress { Step = name, Finished = true, Success = false, Detail = "Cancelled" });
                    report.Errors.Add($"{name} was cancelled.");
                    // Try to leave the image in a safe state: discard the mount.
                    try
                    {
                        await _dism.UnmountImageAsync(mountDir, commit: false).ConfigureAwait(false);
                    }
                    catch
                    {
                        // best effort
                    }

                    throw;
                }
                catch (Exception ex)
                {
                    stepWatch.Stop();
                    report.Steps.Add(new BuildStepResult(name, false, ex.Message, stepWatch.Elapsed));
                    progress?.Report(new BuildProgress { Step = name, Finished = true, Success = false, Detail = ex.Message });
                    report.Errors.Add($"{name}: {ex.Message}");
                    _log.Error("Build", $"Step failed: {name}", ex);
                    try
                    {
                        await _dism.UnmountImageAsync(mountDir, commit: false).ConfigureAwait(false);
                    }
                    catch
                    {
                        // best effort — never leave a mounted image behind
                    }

                    break;
                }
            }
        }
        finally
        {
            stopwatch.Stop();
            report.FinishedUtc = DateTimeOffset.UtcNow;
            report.Success = report.Errors.Count == 0 && report.Steps.All(s => s.Success);
            await WriteReportAsync(project, report).ConfigureAwait(false);
        }

        return report;
    }

    // ------------------------------------------------------- helpers -------------------------------------------------------

    /// <summary>
    /// Returns a mount directory that is guaranteed to sit on an NTFS volume.
    /// DISM /Mount-Image creates reparse points and fails with 0xc142011f on FAT/exFAT/ReFS.
    /// When the project drive is not NTFS the mount point is relocated to the user's
    /// local application data (C: is virtually always NTFS) with a clear report note.
    /// </summary>
    private string ResolveNtfsMountDir(string preferred, BuildReport report)
    {
        if (IsNtfs(preferred, out var preferredFormat, out var preferredRoot))
        {
            return preferred;
        }

        var fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WindowsCustomizer", "Mount", "image");

        if (!IsNtfs(fallback, out var fallbackFormat, out var fallbackRoot))
        {
            throw new InvalidOperationException(
                $"DISM /Mount-Image requires an NTFS volume (reparse points). " +
                $"{preferredRoot} is {preferredFormat} and {fallbackRoot} is {fallbackFormat}. " +
                "Move the project workspace to an NTFS drive and run the build again.");
        }

        report.Warnings.Add(
            $"The project drive {preferredRoot} uses {preferredFormat}, which does not support the reparse points DISM needs for mounting. " +
            $"The image is mounted at {fallback} (NTFS) instead — all other build data stays in the project workspace.");
        _log.Warn("Build", $"Mount point relocated from '{preferred}' ({preferredFormat}) to '{fallback}' (NTFS).");
        return fallback;
    }

    private static bool IsNtfs(string path, out string format, out string root)
    {
        format = "unknown";
        root = path;
        try
        {
            root = Path.GetPathRoot(Path.GetFullPath(path)) ?? path;
            format = new DriveInfo(root).DriveFormat;
            return format.Equals("NTFS", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private async Task<string> LocateInstallImageAsync(ProjectModel project, string cacheDir)
    {
        // Extract just the install payload from the ISO into Images/ (read-only source).
        await Task.CompletedTask;
        return InstallPayloadExtractor.Ensure(project, _log);
    }

    private static DiscUtils.DiscFileSystem OpenIso(FileStream stream)
    {
        try
        {
            stream.Seek(0, SeekOrigin.Begin);
            return new DiscUtils.Udf.UdfReader(stream);
        }
        catch
        {
            stream.Seek(0, SeekOrigin.Begin);
            return new DiscUtils.Iso9660.CDReader(stream, joliet: true, hideVersions: true);
        }
    }

    private static void ExtractIsoContent(string isoPath, string destDir)
    {
        Directory.CreateDirectory(destDir);
        using var stream = new FileStream(isoPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var fs = OpenIso(stream);
        ExtractRecursive(fs, "\\", destDir);
    }

    private static void ExtractRecursive(DiscUtils.DiscFileSystem fs, string isoDir, string destDir)
    {
        foreach (var dir in fs.GetDirectories(isoDir))
        {
            var name = Path.GetFileName(dir.TrimEnd('\\', '/'));
            ExtractRecursive(fs, dir, Path.Combine(destDir, name));
        }

        foreach (var file in fs.GetFiles(isoDir))
        {
            var name = fs.GetFileInfo(file).Name;
            var cleanName = name.Contains(';') ? name[..name.IndexOf(';')] : name;
            using var src = fs.OpenFile(file, FileMode.Open, FileAccess.Read);
            using var dst = File.Create(Path.Combine(destDir, cleanName));
            src.CopyTo(dst);
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var dir in Directory.GetDirectories(source))
        {
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
        }

        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        }
    }

    private static string GenerateAppInstallScript(IReadOnlyList<AppInstallEntry> apps)
    {
        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine("rem Windows Customizer Studio - automatic application installs");
        foreach (var app in apps)
        {
            sb.AppendLine($"echo Installing {app.Name}...");
            sb.AppendLine($"start /wait \"\" \"%~dp0{Path.GetFileName(app.InstallerPath)}\" {app.SilentArguments}");
            sb.AppendLine("if errorlevel 1 echo WARNING: {app.Name} installer returned an error.");
        }

        return sb.ToString();
    }

    private static string GenerateOemInfo(BrandingSettings b)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[General]");
        sb.AppendLine($"Manufacturer={b.Manufacturer}");
        sb.AppendLine($"Model={b.Model}");
        sb.AppendLine($"SupportURL={b.SupportUrl}");
        sb.AppendLine($"SupportPhone={b.SupportPhone}");
        return sb.ToString();
    }

    private async Task WriteReportAsync(ProjectModel project, BuildReport report)
    {
        try
        {
            var dir = Path.Combine(project.WorkspacePath, "Logs");
            Directory.CreateDirectory(dir);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var jsonPath = Path.Combine(dir, $"build-report-{stamp}.json");
            await File.WriteAllTextAsync(jsonPath,
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(false);

            var mdPath = Path.Combine(dir, $"build-report-{stamp}.md");
            await File.WriteAllTextAsync(mdPath, BuildReportMarkdown(report)).ConfigureAwait(false);
            _log.Info("Build", $"Report written: {mdPath}");
        }
        catch (Exception ex)
        {
            _log.Warn("Build", $"Could not write build report: {ex.Message}");
        }
    }

    public static string BuildReportMarkdown(BuildReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Build Report — {report.ProjectName}");
        sb.AppendLine();
        sb.AppendLine($"- **Result:** {(report.Success ? "√ SUCCESS" : "✕ FAILED")}");
        sb.AppendLine($"- **Started:** {report.StartedUtc:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"- **Finished:** {report.FinishedUtc:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"- **Duration:** {(report.FinishedUtc - report.StartedUtc).TotalMinutes:0.0} minutes");
        if (!string.IsNullOrEmpty(report.OutputIso))
        {
            sb.AppendLine($"- **Output ISO:** `{report.OutputIso}`");
            sb.AppendLine($"- **Strategy:** {report.Strategy}");
        }

        sb.AppendLine();
        sb.AppendLine("## Steps");
        sb.AppendLine();
        sb.AppendLine("| # | Step | Status | Duration | Detail |");
        sb.AppendLine("|---|------|--------|----------|--------|");
        var i = 1;
        foreach (var s in report.Steps)
        {
            sb.AppendLine($"| {i++} | {s.Name} | {(s.Success ? "√" : "✕")} | {s.Duration.TotalSeconds:0}s | {s.Detail.Replace("|", "/")} |");
        }

        if (report.Warnings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Warnings");
            foreach (var w in report.Warnings)
            {
                sb.AppendLine($"- ⚠️ {w}");
            }
        }

        if (report.Errors.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Errors");
            foreach (var e in report.Errors)
            {
                sb.AppendLine($"- ✕ {e}");
            }
        }

        if (report.ArtifactHashes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Artifact Hashes");
            foreach (var h in report.ArtifactHashes)
            {
                sb.AppendLine($"- `{h}`");
            }
        }

        return sb.ToString();
    }
}

/// <summary>Script staging (spec 19): generates launcher scripts per run phase.</summary>
public static class ScriptEngine
{
    public static IEnumerable<(string FileName, string Content)> GenerateScriptLaunchers(IReadOnlyList<ScriptEntry> scripts)
    {
        var firstBoot = scripts.Where(s => s.Phase == ScriptRunPhase.FirstBoot || s.Phase == ScriptRunPhase.FirstUserLogin).ToList();
        if (firstBoot.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("rem Generated by Windows Customizer Studio - first boot tasks");
            foreach (var s in firstBoot)
            {
                sb.AppendLine(s.Kind switch
                {
                    "PowerShell" => $"powershell -NoProfile -ExecutionPolicy Bypass -File \"%~dp0{s.FileName}\"",
                    "Registry" => $"regedit /s \"%~dp0{s.FileName}\"",
                    _ => $"call \"%~dp0{s.FileName}\""
                });
            }

            yield return ("FirstLogon.cmd", sb.ToString());
        }

        var specialize = scripts.Where(s => s.Phase is ScriptRunPhase.BeforeInstallation or ScriptRunPhase.DuringInstallation).ToList();
        if (specialize.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("rem Generated by Windows Customizer Studio - specialize tasks");
            foreach (var s in specialize)
            {
                sb.AppendLine(s.Kind switch
                {
                    "PowerShell" => $"powershell -NoProfile -ExecutionPolicy Bypass -File \"%~dp0{s.FileName}\"",
                    "Registry" => $"regedit /s \"%~dp0{s.FileName}\"",
                    _ => $"call \"%~dp0{s.FileName}\""
                });
            }

            yield return ("Specialize.cmd", sb.ToString());
        }
    }
}

internal static class CountExtensions
{
    public static int CountIfEnabled(this IEnumerable<RegistryTweakEntry> tweaks) => tweaks.Count(t => t.Enabled);
}
