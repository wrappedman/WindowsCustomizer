using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WindowsCustomizer.App.Infrastructure;
using WindowsCustomizer.Core.Engines;
using WindowsCustomizer.Core.Logging;
using WindowsCustomizer.Core.Models;
using WindowsCustomizer.Core.Services;

namespace WindowsCustomizer.App.ViewModels;

// ======================================================== REVIEW (spec 27/28/29/30) ========================================================

public sealed partial class ReviewViewModel : CustomizationPageViewModel
{
    private readonly DependencyEngine _dependency;
    private readonly ProjectSnapshotService _snapshots;

    [ObservableProperty] private string _riskSummary = string.Empty;
    [ObservableProperty] private string _changeSummary = string.Empty;

    public ReviewViewModel(
        ProjectModel project,
        DependencyEngine dependency,
        ProjectSnapshotService snapshots,
        IAppLogger logger,
        IDialogService dialogs,
        IStatusReporter status)
        : base(project, logger, dialogs, status)
    {
        _dependency = dependency;
        _snapshots = snapshots;
        Title = "Review Changes";
        Subtitle = "Every change the build will make, with risk levels and dependency checks. Toggle anything off before building.";
        Refresh();
    }

    public ObservableCollection<ReviewItemViewModel> Changes { get; } = new();

    public ObservableCollection<DependencyIssue> Issues { get; } = new();

    public RiskLevel MaxRisk { get; private set; }

    [RelayCommand]
    private void Refresh()
    {
        Changes.Clear();
        var state = State;

        void Add(string group, string text, bool enabled, RiskLevel risk, Action<bool> toggle, string detail = "")
            => Changes.Add(new ReviewItemViewModel(group, text, enabled, risk, toggle, detail));

        Add("User", $"Default username: {state.User.DefaultUsername}", true, RiskLevel.Safe, _ => { });
        Add("User", $"Computer name: {state.User.ComputerName}", true, RiskLevel.Safe, _ => { });
        if (state.User.AutoLogin)
        {
            Add("User", "Auto login enabled", true, RiskLevel.Medium, _ => { });
        }

        if (state.Unattend.GenerateAutounattend)
        {
            Add("Setup", "Generate Autounattend.xml (unattended setup)", true, RiskLevel.Low, _ => { });
        }

        if (state.Unattend.ConfigureDisk)
        {
            Add("Setup", $"Repartition target disk ({state.Unattend.DiskLayout}) — WIPES THE TARGET DRIVE", true, RiskLevel.Critical, _ => { });
        }

        foreach (var a in state.AppRemovals.Where(a => a.Mode == AppRemovalMode.Remove))
        {
            Add("Apps", $"Remove {a.DisplayName}", a.Enabled, a.IsSensitive ? RiskLevel.High : RiskLevel.Medium,
                v => a.Enabled = v, a.PackageName);
        }

        foreach (var t in state.RegistryTweaks.Where(t => t.Category != "Services"))
        {
            Add("Registry", $"{t.Description}", t.Enabled, t.Risk, v => t.Enabled = v, $"{t.Hive}\\{t.KeyPath} [{t.ValueName}]");
        }

        foreach (var s in state.Services.Where(s => s.Enabled))
        {
            Add("Services", $"{s.DisplayName} → {s.Startup}", s.Enabled, s.IsCritical ? RiskLevel.High : RiskLevel.Medium,
                v => s.Enabled = v);
        }

        foreach (var d in state.Drivers)
        {
            Add("Drivers", $"Add {d.DisplayName}", d.Enabled, RiskLevel.Low, v => d.Enabled = v, d.SourcePath);
        }

        foreach (var u in state.Updates)
        {
            Add("Updates", $"Add {u.KbNumber} ({u.UpdateType})", u.Enabled, RiskLevel.Low, v => u.Enabled = v);
        }

        foreach (var f in state.Features)
        {
            Add("Features", $"{(f.Enable ? "Enable" : f.Remove ? "Remove" : "Skip")} {f.DisplayName}", f.Enabled,
                f.Remove ? RiskLevel.Medium : RiskLevel.Low, v => f.Enabled = v);
        }

        foreach (var sc in state.Scripts)
        {
            Add("Scripts", $"[{sc.Phase}] {sc.FileName}", sc.Enabled, RiskLevel.High, v => sc.Enabled = v);
        }

        foreach (var fc in state.FileCopies)
        {
            Add("Files", $"Copy {Path.GetFileName(fc.SourcePath)} → {fc.TargetPath}", fc.Enabled, RiskLevel.Safe, v => fc.Enabled = v);
        }

        foreach (var ap in state.Applications.OrderBy(a => a.InstallOrder))
        {
            Add("Apps", $"Install {ap.Name} {ap.Version}", ap.Enabled, RiskLevel.Medium, v => ap.Enabled = v);
        }

        Issues.Clear();
        foreach (var issue in _dependency.Analyze(state))
        {
            Issues.Add(issue);
        }

        MaxRisk = _dependency.MaxRisk(state);
        RiskSummary = $"Maximum risk level: {MaxRisk}" +
                      (MaxRisk >= RiskLevel.Critical ? " — explicit confirmation will be required to build." : string.Empty);
        ChangeSummary = $"{Changes.Count} change(s), {Issues.Count} dependency note(s).";
    }

    [RelayCommand]
    private void SaveReview()
    {
        _snapshots.TakeSnapshot(Project, "Before build (review saved)");
        Save();
        Status.SetStatus("Review saved + snapshot taken. You can roll back at any time.");
    }

    [RelayCommand]
    private void Undo()
    {
        try
        {
            _snapshots.UndoLastChange(Project);
            Refresh();
            Status.SetStatus("Rolled back to the previous snapshot.");
        }
        catch (Exception ex)
        {
            Dialogs.ShowError("Undo", UserError.FromException(ex, "Rollback failed."));
        }
    }
}

public sealed partial class ReviewItemViewModel : ObservableObject
{
    public ReviewItemViewModel(string group, string text, bool enabled, RiskLevel risk, Action<bool> toggle, string detail)
    {
        Group = group;
        Text = text;
        _isEnabled = enabled;
        Risk = risk;
        _toggle = toggle;
        Detail = detail;
    }

    public string Group { get; }
    public string Text { get; }
    public string Detail { get; }
    public RiskLevel Risk { get; }
    private readonly Action<bool> _toggle;

    [ObservableProperty]
    private bool _isEnabled;

    public string RiskLabel => Risk.ToString();

    partial void OnIsEnabledChanged(bool value) => _toggle(value);
}

// ======================================================== BUILD (spec 32/33/35/36) ========================================================

public sealed partial class BuildViewModel : CustomizationPageViewModel
{
    private readonly BuildPipeline _pipeline;
    private readonly DependencyEngine _dependency;
    private readonly ProjectSnapshotService _snapshots;
    private readonly ValidationEngine _validation;
    private CancellationTokenSource? _cts;

    [ObservableProperty] private bool _isBuilding;
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private string _currentStep = "Ready.";
    [ObservableProperty] private string _spaceEstimate = string.Empty;
    [ObservableProperty] private string _reportSummary = string.Empty;
    [ObservableProperty] private string _outputPath = string.Empty;
    [ObservableProperty] private string _vmStatus = string.Empty;

    public BuildViewModel(
        ProjectModel project,
        BuildPipeline pipeline,
        DependencyEngine dependency,
        ProjectSnapshotService snapshots,
        ValidationEngine validation,
        IAppLogger logger,
        IDialogService dialogs,
        IStatusReporter status)
        : base(project, logger, dialogs, status)
    {
        _pipeline = pipeline;
        _dependency = dependency;
        _snapshots = snapshots;
        _validation = validation;
        Title = "Build";
        Subtitle = "Validate → Mount → Customize → Commit → Bootable ISO → Validate → Report. Every step is logged.";
        Steps = new ObservableCollection<BuildStepResult>();

        var (s, w, t, o, req) = BuildPipeline.EstimateSpace(project);
        SpaceEstimate =
            $"Source ISO: {Gb(s)} · Working image: {Gb(w)} · Temporary: {Gb(t)} · Output: {Gb(o)}\n" +
            $"Required free space: ~{Gb(req)}";
    }

    public ObservableCollection<BuildStepResult> Steps { get; }

    [RelayCommand]
    private async Task BuildAsync()
    {
        if (IsBuilding)
        {
            return;
        }

        var blockers = _dependency.Analyze(Project.Customization)
            .Where(i => i.Severity == IssueSeverity.Blocker).ToList();
        if (blockers.Count > 0)
        {
            // Auto-login without password is a convenience setting — offer an instant fix so the
            // build is never stuck on it.
            var onlyAutologon = blockers.Count == 1 && blockers[0].Code == "AUTOLOGON_NO_PASSWORD";
            if (onlyAutologon &&
                Dialogs.Confirm("Build",
                    "Auto-login is enabled but no password is set.\n\n" +
                    "Turn OFF Auto Login and continue building?\n" +
                    "(You can also set a password on the Users page and keep Auto Login.)"))
            {
                Project.Customization.User.AutoLogin = false;
                new WindowsCustomizer.Core.Services.ProjectService(Logger).SaveProject(Project);
            }
            else
            {
                Dialogs.ShowError("Build", new UserError(
                    "The project has blocking dependency issues.",
                    string.Join("\n", blockers.Select(b => b.Message)),
                    string.Join("\n", blockers.Select(b => "→ " + b.Resolution)) +
                    "\n\nThese are also listed with ⚠ on the page where they are configured (e.g. Users), " +
                    "then build again."));
                return;
            }
        }

        var maxRisk = _dependency.MaxRisk(Project.Customization);
        if (maxRisk >= RiskLevel.Critical &&
            !Dialogs.Confirm("Critical changes",
                "This build contains CRITICAL changes (e.g. disk repartitioning or disabling critical services).\n\n" +
                "The resulting image can destroy data or break Windows. Build anyway?"))
        {
            return;
        }

        _snapshots.TakeSnapshot(Project, "Automatic snapshot before build");
        _cts = new CancellationTokenSource();
        IsBuilding = true;
        Steps.Clear();
        ReportSummary = string.Empty;

        var started = new Dictionary<string, DateTime>();
        var progress = new Progress<BuildProgress>(p =>
        {
            if (!p.Finished)
            {
                started[p.Step] = DateTime.UtcNow;
                Steps.Add(new BuildStepResult(p.Step, true, "In progress…", TimeSpan.Zero));
                CurrentStep = $"{p.Step}…";
            }
            else
            {
                var duration = started.TryGetValue(p.Step, out var t) ? DateTime.UtcNow - t : TimeSpan.Zero;
                var row = new BuildStepResult(p.Step, p.Success, p.Detail, duration);
                var idx = -1;
                for (var i = Steps.Count - 1; i >= 0; i--)
                {
                    if (Steps[i].Name == p.Step)
                    {
                        idx = i;
                        break;
                    }
                }

                if (idx >= 0)
                {
                    Steps[idx] = row;
                }
                else
                {
                    Steps.Add(row);
                }

                CurrentStep = p.Success ? $"{p.Step}: {p.Detail}" : $"{p.Step} failed: {p.Detail}";
            }

            Status.SetStatus($"Build: {p.Step}");
            var done = Steps.Count(s => s.Detail != "In progress…");
            ProgressValue = Math.Min(99, done * (100.0 / Math.Max(1, Steps.Count)));
        });

        try
        {
            var report = await _pipeline.BuildAsync(Project, progress, _cts.Token).ConfigureAwait(true);
            // Live rows are already in Steps — fill the final duration/detail from the report.
            foreach (var s in report.Steps)
            {
                for (var i = 0; i < Steps.Count; i++)
                {
                    if (Steps[i].Name == s.Name && Steps[i].Detail != s.Detail)
                    {
                        Steps[i] = s;
                        break;
                    }
                }
            }

            OutputPath = report.OutputIso;
            ReportSummary = BuildPipeline.BuildReportMarkdown(report);
            ProgressValue = report.Success ? 100 : 0;
            CurrentStep = report.Success
                ? $"Build completed in {(report.FinishedUtc - report.StartedUtc).TotalMinutes:0.0} min."
                : "Build failed — see the report below.";
            Status.SetStatus(CurrentStep);

            if (report.Success)
            {
                Dialogs.ShowInfo("Build complete",
                    $"Your customized Windows ISO is ready:\n\n{report.OutputIso}\n\n" +
                    "Open the Output page to validate it or test it in a VM.");
            }
        }
        catch (OperationCanceledException)
        {
            CurrentStep = "Build cancelled — the image was discarded safely (no partial commit).";
            Status.SetStatus("Build cancelled.");
        }
        catch (Exception ex)
        {
            Logger.Error("Build", "Build failed.", ex);
            CurrentStep = "Build failed: " + ex.Message;
            Dialogs.ShowError("Build", UserError.FromException(ex, "The build failed.",
                "Check the step list and log for details. The working image was discarded; the source ISO is untouched."));
        }
        finally
        {
            IsBuilding = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
        CurrentStep = "Cancelling — waiting for a safe point...";
    }

    [RelayCommand]
    private void OpenOutputFolder()
    {
        var dir = Path.Combine(Project.WorkspacePath, "Output");
        if (Directory.Exists(dir))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
        }
    }

    [RelayCommand]
    private async Task ValidateOutputAsync()
    {
        if (string.IsNullOrEmpty(OutputPath))
        {
            OutputPath = Path.Combine(Project.WorkspacePath, "Output", Project.Settings.OutputFileName);
        }

        try
        {
            Status.SetStatus("Validating output ISO...");
            var issues = await _validation.ValidateOutputIsoAsync(OutputPath).ConfigureAwait(true);
            var sb = new StringBuilder();
            foreach (var i in issues)
            {
                sb.AppendLine($"[{i.Severity}] {i.Message}");
            }

            ReportSummary = "## Validation\n\n" + (sb.Length == 0 ? "All checks passed." : sb.ToString());
            Status.SetStatus("Validation finished.");
        }
        catch (Exception ex)
        {
            Dialogs.ShowError("Validation", UserError.FromException(ex, "Validation failed."));
        }
    }

    [RelayCommand]
    private void TestInVm()
    {
        // Spec 35: optional VM testing. Detects Hyper-V / QEMU and reports how to test.
        var qemu = FindOnPath("qemu-system-x86_64.exe") ?? FindOnPath("qemu-system-x86_64");
        var hyperv = FindOnPath("hvc.exe") is not null;

        if (hyperv)
        {
            VmStatus = "Hyper-V detected. Create a Generation 2 VM, attach the ISO from the Output folder and boot it.";
        }
        else if (qemu is not null)
        {
            VmStatus = $"QEMU detected ({qemu}). Run:\nqemu-system-x86_64 -m 4096 -cdrom \"{OutputPath}\" -boot d";
        }
        else
        {
            VmStatus = "No Hyper-V or QEMU detected. Install Hyper-V or QEMU to test the ISO in a virtual machine.";
        }

        Dialogs.ShowInfo("Test in VM", VmStatus);
    }

    private static string? FindOnPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        return path.Split(Path.PathSeparator)
            .Select(d => Path.Combine(d, name))
            .FirstOrDefault(File.Exists);
    }

    private static string Gb(long bytes) => $"{bytes / 1_000_000_000.0:0.0} GB";
}
