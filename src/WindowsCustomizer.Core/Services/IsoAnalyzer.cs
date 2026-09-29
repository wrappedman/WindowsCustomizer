using System.Diagnostics;
using System.Text;
using WindowsCustomizer.Core.Logging;
using WindowsCustomizer.Core.Models;
using WindowsCustomizer.Core.Utilities;

namespace WindowsCustomizer.Core.Services;

/// <summary>Read-only analyzer for Windows installation ISO files.</summary>
public interface IIsoAnalyzer
{
    Task<IsoAnalysisResult> AnalyzeAsync(
        string isoPath,
        IProgress<AnalysisProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Inspects an ISO without modifying it and without mounting anything: detects volume label,
/// file system, boot files (BIOS + UEFI), the install.wim/install.esd payload and its header.
/// Implemented on DiscUtils (pure managed) so analysis is safe, fast and testable.
/// </summary>
public sealed class IsoAnalyzer : IIsoAnalyzer
{
    private const int SourcesListingLimit = 250;

    private static readonly (string Path, BootFileKind Kind)[] KnownBootFiles =
    {
        (@"\bootmgr", BootFileKind.BiosBootManager),
        (@"\boot\etfsboot.com", BootFileKind.BiosBootSector),
        (@"\efi\boot\bootx64.efi", BootFileKind.EfiX64),
        (@"\efi\boot\bootia32.efi", BootFileKind.EfiX86),
        (@"\efi\boot\bootaa64.efi", BootFileKind.EfiArm64),
        (@"\setup.exe", BootFileKind.SetupEntry)
    };

    private readonly IAppLogger _log;

    public IsoAnalyzer(IAppLogger log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public async Task<IsoAnalysisResult> AnalyzeAsync(
        string isoPath,
        IProgress<AnalysisProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new IsoAnalysisResult
        {
            IsoPath = Path.GetFullPath(isoPath),
            FileName = Path.GetFileName(isoPath),
            AnalyzedUtc = DateTimeOffset.UtcNow
        };

        try
        {
            ValidateInput(isoPath, result);

            result.FileSizeBytes = new FileInfo(result.IsoPath).Length;
            if (result.FileSizeBytes <= 0)
            {
                result.Errors.Add("The selected file is empty.");
                return result;
            }

            _log.Info("IsoAnalyzer", $"Analyzing '{result.FileName}' ({FormatBytes(result.FileSizeBytes)})");

            // File I/O and stream parsing are synchronous; keep the UI responsive by hopping threads.
            await Task.Run(() => AnalyzeCore(result, progress, cancellationToken), cancellationToken).ConfigureAwait(false);

            FinalizeHints(result);
            result.AnalyzedUtc = DateTimeOffset.UtcNow;
            _log.Success("IsoAnalyzer", $"Analysis finished: label '{result.VolumeLabel}', " +
                                        $"{result.InstallImageFormat} payload, {result.BootFiles.Count} boot file(s).");
        }
        catch (OperationCanceledException)
        {
            result.Warnings.Add("Analysis was cancelled by the user.");
            _log.Warn("IsoAnalyzer", "Analysis cancelled.");
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Unexpected error while analyzing the ISO: {ex.Message}");
            _log.Error("IsoAnalyzer", "Analysis failed.", ex);
        }
        finally
        {
            stopwatch.Stop();
            result.Elapsed = stopwatch.Elapsed;
        }

        return result;
    }

    private static void ValidateInput(string isoPath, IsoAnalysisResult result)
    {
        if (string.IsNullOrWhiteSpace(isoPath))
        {
            result.Errors.Add("No ISO path was provided.");
            return;
        }

        if (!File.Exists(isoPath))
        {
            result.Errors.Add($"File not found: {isoPath}");
            return;
        }

        if (!string.Equals(Path.GetExtension(isoPath), ".iso", StringComparison.OrdinalIgnoreCase))
        {
            result.Warnings.Add("The file extension is not '.iso' — continuing anyway.");
        }
    }

    private void AnalyzeCore(IsoAnalysisResult result, IProgress<AnalysisProgress>? progress, CancellationToken ct)
    {
        progress?.Report(new AnalysisProgress("Opening ISO", 5));

        using var stream = new FileStream(result.IsoPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1024 * 128, FileOptions.SequentialScan);
        var fs = OpenFileSystem(stream, result);

        result.VolumeLabel = Safe(() => fs.VolumeLabel, "(unknown)");
        if (string.IsNullOrWhiteSpace(result.VolumeLabel))
        {
            result.VolumeLabel = "(no label)";
        }

        ct.ThrowIfCancellationRequested();
        progress?.Report(new AnalysisProgress("Reading structure", 20));

        foreach (var entry in ListEntries(fs, "\\"))
        {
            result.RootEntries.Add(entry);
        }

        progress?.Report(new AnalysisProgress("Detecting boot files", 40));

        foreach (var (path, kind) in KnownBootFiles)
        {
            ct.ThrowIfCancellationRequested();
            if (!Safe(() => fs.FileExists(path), false))
            {
                continue;
            }

            var size = Safe(() => fs.GetFileInfo(path).Length, 0L);
            result.BootFiles.Add(new BootFileInfo { Path = path, Kind = kind });
            _log.Debug("IsoAnalyzer", $"Boot file: {path} ({FormatBytes(size)})");
        }

        foreach (var arch in ArchitecturesFromBootFiles(result.BootFiles))
        {
            result.Architectures.Add(arch);
        }

        // setup.exe alone does not make media bootable — look for real boot loaders.
        if (!result.BootFiles.Any(b => b.Kind is BootFileKind.BiosBootManager or BootFileKind.BiosBootSector
                or BootFileKind.EfiX64 or BootFileKind.EfiX86 or BootFileKind.EfiArm64))
        {
            result.Warnings.Add("No known Windows boot files were found — is this really a Windows installation ISO?");
        }

        progress?.Report(new AnalysisProgress("Inspecting install image", 60));
        InspectInstallImage(fs, result, ct);

        progress?.Report(new AnalysisProgress("Listing sources", 80));
        if (Safe(() => fs.DirectoryExists(@"\sources"), false))
        {
            foreach (var entry in ListEntries(fs, @"\sources"))
            {
                if (result.SourcesFiles.Count >= SourcesListingLimit)
                {
                    result.Warnings.Add($"The 'sources' folder contains more than {SourcesListingLimit} entries; listing truncated.");
                    break;
                }

                result.SourcesFiles.Add(entry);
            }
        }

        progress?.Report(new AnalysisProgress("Done", 100));
    }

    private static DiscUtils.DiscFileSystem OpenFileSystem(FileStream stream, IsoAnalysisResult result)
    {
        var kinds = new List<string>();
        Exception? lastError = null;

        // Windows install media is normally UDF (with an ISO9660 shadow). Try UDF first.
        try
        {
            stream.Seek(0, SeekOrigin.Begin);
            var udf = new DiscUtils.Udf.UdfReader(stream);
            kinds.Add("UDF");
            result.FileSystem = "UDF";
            return udf;
        }
        catch (Exception ex)
        {
            lastError = ex;
        }

        try
        {
            stream.Seek(0, SeekOrigin.Begin);
            // hideVersions strips ISO9660 ";1" version suffixes so plain names resolve.
            var iso = new DiscUtils.Iso9660.CDReader(stream, joliet: true, hideVersions: true);
            kinds.Add("ISO9660");
            result.FileSystem = string.Join("+", kinds);
            return iso;
        }
        catch (Exception ex)
        {
            lastError = ex;
        }

        throw new InvalidOperationException(
            "The file could not be opened as UDF or ISO9660. It may be corrupt or not an ISO image.",
            lastError);
    }

    private void InspectInstallImage(DiscUtils.DiscFileSystem fs, IsoAnalysisResult result, CancellationToken ct)
    {
        var candidates = new (string Path, InstallImageFormat Format)[]
        {
            (@"\sources\install.wim", InstallImageFormat.Wim),
            (@"\sources\install.esd", InstallImageFormat.Esd),
            (@"\sources\install.swm", InstallImageFormat.Swm)
        };

        foreach (var (path, format) in candidates)
        {
            if (!Safe(() => fs.FileExists(path), false))
            {
                continue;
            }

            result.InstallImageFormat = format;
            result.InstallImagePath = path;
            result.InstallImageSize = Safe(() => fs.GetFileInfo(path).Length, 0L);
            result.InstallImageHeader = TryReadWimHeader(fs, path);
            result.Editions = TryReadEditions(fs, path);
            result.EditionNames = result.Editions.Select(e => e.Name).ToList();
            _log.Info("IsoAnalyzer",
                $"Install payload: {path} ({format}, {FormatBytes(result.InstallImageSize)}, " +
                $"{(result.InstallImageHeader is null ? "header unreadable" : $"{result.InstallImageHeader.ImageCount} image(s), {result.InstallImageHeader.Compression}")}).");
            if (result.EditionNames.Count > 0)
            {
                _log.Info("IsoAnalyzer", "Editions: " + string.Join(", ", result.EditionNames));
            }

            break;
        }

        if (result.InstallImageFormat == InstallImageFormat.None)
        {
            result.Errors.Add("No sources\\install.wim, install.esd or install.swm was found — the ISO does not contain a Windows install payload.");
        }
        else if (result.InstallImageFormat == InstallImageFormat.Esd)
        {
            result.Warnings.Add("install.esd is solid-compressed: it must be exported to WIM before servicing (the build engine does this automatically).");
        }

        ct.ThrowIfCancellationRequested();

        if (Safe(() => fs.FileExists(@"\sources\boot.wim"), false))
        {
            result.BootImageFormat = InstallImageFormat.Wim;
            result.BootImagePath = @"\sources\boot.wim";
            result.BootImageHeader = TryReadWimHeader(fs, @"\sources\boot.wim");
        }
    }

    private WimHeaderSummary? TryReadWimHeader(DiscUtils.DiscFileSystem fs, string path)
    {
        try
        {
            using var s = fs.OpenFile(path, FileMode.Open, FileAccess.Read);
            var summary = WimHeaderReader.TryRead(s);
            if (summary is null)
            {
                _log.Warn("IsoAnalyzer", $"Could not parse the WIM header of {path}.");
            }

            return summary;
        }
        catch (Exception ex)
        {
            _log.Warn("IsoAnalyzer", $"Failed to read {path}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Reads edition entries straight from the WIM XML manifest (no extraction needed).</summary>
    private List<EditionOption> TryReadEditions(DiscUtils.DiscFileSystem fs, string path)
    {
        try
        {
            using var s = fs.OpenFile(path, FileMode.Open, FileAccess.Read);
            var images = WimXmlReader.TryReadImages(s);
            return images?
                .Select(i => new EditionOption(i.Index, i.Name, i.EditionId))
                .Where(e => !string.IsNullOrWhiteSpace(e.Name))
                .ToList() ?? new List<EditionOption>();
        }
        catch (Exception ex)
        {
            _log.Warn("IsoAnalyzer", $"Edition manifest unreadable for {path}: {ex.Message}");
            return new List<EditionOption>();
        }
    }

    private static IEnumerable<IsoFileEntry> ListEntries(DiscUtils.DiscFileSystem fs, string directory)
    {
        var entries = new List<IsoFileEntry>();
        try
        {
            foreach (var dir in fs.GetDirectories(directory))
            {
                entries.Add(new IsoFileEntry
                {
                    Path = dir.Replace('/', '\\'),
                    Name = Path.GetFileName(dir.TrimEnd('\\', '/')),
                    IsDirectory = true
                });
            }

            foreach (var file in fs.GetFiles(directory))
            {
                var info = fs.GetFileInfo(file);
                entries.Add(new IsoFileEntry
                {
                    Path = file.Replace('/', '\\'),
                    Name = info.Name,
                    SizeBytes = info.Length,
                    IsDirectory = false
                });
            }
        }
        catch (Exception)
        {
            // Listing is best-effort; individual unreadable entries must not fail the analysis.
        }

        return entries
            .OrderByDescending(e => e.IsDirectory)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<WindowsArchitecture> ArchitecturesFromBootFiles(IEnumerable<BootFileInfo> bootFiles)
    {
        var set = new SortedSet<WindowsArchitecture>();
        foreach (var file in bootFiles)
        {
            switch (file.Kind)
            {
                case BootFileKind.EfiX64:
                case BootFileKind.EfiX86:
                case BootFileKind.BiosBootManager:
                case BootFileKind.BiosBootSector:
                    set.Add(WindowsArchitecture.X64);
                    break;
                case BootFileKind.EfiArm64:
                    set.Add(WindowsArchitecture.Arm64);
                    break;
            }
        }

        // BIOS boot files imply x86-family; the exact arch of the payload is confirmed in Phase 2 via WIM XML.
        return set.ToList();
    }

    private static void FinalizeHints(IsoAnalysisResult result)
    {
        if (result.BootFiles.Any(b => b.Kind == BootFileKind.EfiX64) &&
            result.BootFiles.Any(b => b.Kind == BootFileKind.BiosBootManager))
        {
            result.Hints.Add("Media supports both UEFI and legacy BIOS boot.");
        }
        else if (result.BootFiles.Any(b => b.Kind == BootFileKind.EfiX64))
        {
            result.Hints.Add("Media is UEFI-bootable.");
        }

        if (result.InstallImageFormat == InstallImageFormat.Wim)
        {
            result.Hints.Add("install.wim detected — ready for direct servicing (mount/extract).");
        }

        if (result.EditionNames.Count > 0)
        {
            result.Hints.Add("Editions inside the install image: " + string.Join(", ", result.EditionNames) + ".");
        }
        else if (result.InstallImageHeader is { ImageCount: > 1 })
        {
            result.Hints.Add($"{result.InstallImageHeader.ImageCount} Windows editions are stored inside the install image.");
        }
    }

    private static T Safe<T>(Func<T> func, T fallback)
    {
        try
        {
            return func();
        }
        catch (Exception)
        {
            return fallback;
        }
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
