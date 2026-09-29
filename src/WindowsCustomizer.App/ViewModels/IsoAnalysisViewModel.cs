using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WindowsCustomizer.App.Infrastructure;
using WindowsCustomizer.Core.Logging;
using WindowsCustomizer.Core.Models;
using WindowsCustomizer.Core.Services;
using WindowsCustomizer.Core.Utilities;

namespace WindowsCustomizer.App.ViewModels;

/// <summary>
/// Project workspace page (Phase 1): shows project info and runs the read-only ISO analysis,
/// displaying volume label, boot files, architecture and the install image payload.
/// </summary>
public sealed partial class IsoAnalysisViewModel : ViewModelBase
{
    private readonly ProjectModel _project;
    private readonly IIsoAnalyzer _analyzer;
    private readonly RecentProjectsService _recent;
    private readonly IAppLogger _logger;
    private readonly IDialogService _dialogs;
    private readonly IStatusReporter _status;
    private readonly Action _backToStart;

    private CancellationTokenSource? _cts;
    private IsoAnalysisResult? _result;

    [ObservableProperty]
    private bool _isAnalyzing;

    [ObservableProperty]
    private bool _hasResult;

    [ObservableProperty]
    private string _analysisStatus = "Not analyzed yet.";

    [ObservableProperty]
    private string _projectSummary = string.Empty;

    [ObservableProperty]
    private string _sourceIsoLine = string.Empty;

    [ObservableProperty]
    private string _sha256 = "(not computed)";

    [ObservableProperty]
    private bool _isHashing;

    [ObservableProperty]
    private string _volumeLabel = "—";

    [ObservableProperty]
    private string _fileSystem = "—";

    [ObservableProperty]
    private string _isoSize = "—";

    [ObservableProperty]
    private string _architectures = "—";

    [ObservableProperty]
    private string _bootSummary = "—";

    [ObservableProperty]
    private string _installImageSummary = "—";

    [ObservableProperty]
    private string _installImageHeader = "—";

    [ObservableProperty]
    private string _elapsed = string.Empty;

    /// <summary>Editions inside the install image (read instantly from the WIM XML manifest).</summary>
    public ObservableCollection<EditionOption> EditionOptions { get; } = new();

    [ObservableProperty]
    private EditionOption? _selectedEdition;

    /// <summary>The selected edition is what the build pipeline applies (image index).</summary>
    partial void OnSelectedEditionChanged(EditionOption? value)
    {
        if (value is null)
        {
            return;
        }

        _project.Customization.SelectedImageIndexes = new List<int> { value.Index };
        _logger.Info("IsoAnalysis", $"Edition selected for the build: #{value.Index} {value.Name}");
        _status.SetStatus($"Edition to install: {value.Name} (index {value.Index}).");
    }

    public IsoAnalysisViewModel(
        ProjectModel project,
        IIsoAnalyzer analyzer,
        RecentProjectsService recent,
        IAppLogger logger,
        IDialogService dialogs,
        IStatusReporter status,
        Action backToStart)
    {
        _project = project;
        _analyzer = analyzer;
        _recent = recent;
        _logger = logger;
        _dialogs = dialogs;
        _status = status;
        _backToStart = backToStart;

        Title = project.Name;
        Subtitle = "Project workspace — analyze the source ISO (read-only). Customization steps unlock in later phases.";

        ProjectSummary = $"Workspace: {project.WorkspacePath}";
        SourceIsoLine = $"{project.Source.FileName}  ·  {FormatBytes(project.Source.SizeBytes)}";
        if (!string.IsNullOrEmpty(project.Source.Sha256))
        {
            Sha256 = project.Source.Sha256;
        }

        RootEntries = new ObservableCollection<IsoFileEntry>();
        SourcesFiles = new ObservableCollection<IsoFileEntry>();
        BootFiles = new ObservableCollection<BootFileInfo>();
    }

    public ObservableCollection<IsoFileEntry> RootEntries { get; }

    public ObservableCollection<IsoFileEntry> SourcesFiles { get; }

    public ObservableCollection<BootFileInfo> BootFiles { get; }

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        if (IsAnalyzing)
        {
            return;
        }

        if (!File.Exists(_project.Source.OriginalIsoPath))
        {
            _dialogs.ShowError("Analyze ISO", new UserError(
                "The source ISO file is missing.",
                $"Expected at: {_project.Source.OriginalIsoPath}",
                "Restore the ISO to its original location or update the project to point at a new copy."));
            return;
        }

        IsAnalyzing = true;
        _cts = new CancellationTokenSource();
        var progress = new Progress<AnalysisProgress>(p =>
        {
            AnalysisStatus = $"{p.Stage}... ({p.Percent:0}%)";
            _status.SetProgress(p.Percent, true);
            _status.SetStatus($"ISO analysis: {p.Stage}");
        });

        try
        {
            var result = await _analyzer.AnalyzeAsync(
                _project.Source.OriginalIsoPath, progress, _cts.Token).ConfigureAwait(true);

            _result = result;
            ApplyResult(result);
            _status.SetProgress(100, false);

            if (result.Success)
            {
                AnalysisStatus = $"Analysis completed in {result.Elapsed.TotalSeconds:0.0}s.";
                _status.SetStatus($"ISO analyzed: {result.InstallImageFormat} payload, {result.BootFiles.Count} boot file(s).");

                if (!string.IsNullOrEmpty(_project.Source.Sha256))
                {
                    return;
                }

                // Kick off the checksum automatically once (integrity reference for the project).
                await ComputeHashAsync().ConfigureAwait(true);
            }
            else
            {
                AnalysisStatus = "Analysis finished with errors — see the log for details.";
                _status.SetStatus("ISO analysis reported errors.");
                _dialogs.ShowError("Analyze ISO", new UserError(
                    "The ISO could not be fully analyzed.",
                    string.Join("\n", result.Errors),
                    "Make sure the file is a valid Windows installation ISO. Try re-downloading it if it is corrupt."));
            }
        }
        catch (OperationCanceledException)
        {
            AnalysisStatus = "Analysis cancelled.";
            _status.SetStatus("ISO analysis cancelled.");
        }
        catch (Exception ex)
        {
            _logger.Error("IsoAnalysis", "Analysis failed.", ex);
            _dialogs.ShowError("Analyze ISO", UserError.FromException(ex, "The ISO analysis failed unexpectedly."));
            AnalysisStatus = "Analysis failed.";
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            IsAnalyzing = false;
            _status.SetProgress(0, false);
        }
    }

    [RelayCommand]
    private void CancelAnalysis()
    {
        // Pure read-only analysis: cancelling at any point is safe (spec: Cancellation).
        _cts?.Cancel();
    }

    [RelayCommand]
    private async Task ComputeHashAsync()
    {
        if (IsHashing)
        {
            return;
        }

        try
        {
            IsHashing = true;
            Sha256 = "(computing...)";
            var progress = new Progress<long>(bytes =>
            {
                var pct = _project.Source.SizeBytes > 0 ? bytes * 100.0 / _project.Source.SizeBytes : 0;
                _status.SetProgress(pct, true);
                _status.SetStatus($"Computing SHA-256: {FormatBytes(bytes)} / {FormatBytes(_project.Source.SizeBytes)}");
            });

            var hash = await HashUtil.ComputeSha256Async(_project.Source.OriginalIsoPath, progress, CancellationToken.None)
                .ConfigureAwait(true);

            Sha256 = hash;
            _project.Source.Sha256 = hash;
            _project.Source.HashedUtc = DateTimeOffset.UtcNow;

            // Persist the integrity reference into the project file.
            var saver = new ProjectService(_logger);
            saver.SaveProject(_project);
            _recent.Add(_project);

            _logger.Success("IsoAnalysis", $"SHA-256 of source ISO: {hash}");
            _status.SetStatus("SHA-256 checksum recorded in the project file.");
        }
        catch (Exception ex)
        {
            Sha256 = "(failed)";
            _logger.Error("IsoAnalysis", "Hash computation failed.", ex);
            _dialogs.ShowError("Checksum", UserError.FromException(ex, "The SHA-256 checksum could not be computed."));
        }
        finally
        {
            IsHashing = false;
            _status.SetProgress(0, false);
        }
    }

    [RelayCommand]
    private void OpenLogPanel()
    {
        _status.SetStatus("Use the 'Logs' button in the status bar to toggle the operation log.");
    }

    [RelayCommand]
    private void Back() => _backToStart();

    private void ApplyResult(IsoAnalysisResult result)
    {
        VolumeLabel = result.VolumeLabel;
        FileSystem = result.FileSystem;

        // Persist volume label + analysis timestamp into the project (used by the ISO builder).
        _project.Source.VolumeLabel = result.VolumeLabel;
        _project.Source.AnalyzedUtc = result.AnalyzedUtc;
        _project.Source.InstallImageFormat = result.InstallImageFormat.ToString();
        _project.Source.ImageCount = result.InstallImageHeader?.ImageCount ?? 0;
        _project.Source.ImageCompression = result.InstallImageHeader?.Compression ?? string.Empty;
        IsoSize = FormatBytes(result.FileSizeBytes);
        Architectures = result.Architectures.Count > 0
            ? string.Join(", ", result.Architectures.Select(a => a.ToString()))
            : "Unknown";
        BootSummary = result.BootFiles.Count > 0
            ? string.Join(", ", result.BootFiles.Select(b => b.Kind.ToString()))
            : "None detected";
        Elapsed = $"Analyzed in {result.Elapsed.TotalSeconds:0.0}s at {result.AnalyzedUtc:yyyy-MM-dd HH:mm:ss} UTC";

        if (result.InstallImageFormat == InstallImageFormat.None)
        {
            InstallImageSummary = "Not found";
            InstallImageHeader = "—";
        }
        else
        {
            InstallImageSummary = $"{result.InstallImagePath}  ·  {result.InstallImageFormat}  ·  {FormatBytes(result.InstallImageSize)}";
            InstallImageHeader = result.InstallImageHeader is { } h
                ? $"{h.ImageCount} image(s)  ·  compression: {h.Compression}  ·  WIM v{h.Version}" +
                  (h.IsSpanned ? $"  ·  part {h.PartNumber}/{h.TotalParts}" : string.Empty)
                : "Header could not be parsed";
        }

        RootEntries.Clear();
        foreach (var e in result.RootEntries)
        {
            RootEntries.Add(e);
        }

        SourcesFiles.Clear();
        foreach (var e in result.SourcesFiles)
        {
            SourcesFiles.Add(e);
        }

        // Edition picker (build applies the selected image index).
        EditionOptions.Clear();
        foreach (var e in result.Editions)
        {
            EditionOptions.Add(e);
        }

        var wanted = _project.Customization.SelectedImageIndexes.FirstOrDefault();
        SelectedEdition = EditionOptions.FirstOrDefault(e => e.Index == wanted) ?? EditionOptions.FirstOrDefault();

        BootFiles.Clear();
        foreach (var b in result.BootFiles)
        {
            BootFiles.Add(b);
        }

        foreach (var hint in result.Hints)
        {
            _logger.Info("IsoAnalysis", $"Hint: {hint}");
        }

        foreach (var warning in result.Warnings)
        {
            _logger.Warn("IsoAnalysis", warning);
        }

        HasResult = true;
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
