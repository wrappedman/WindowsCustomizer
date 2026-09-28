using System.Diagnostics;
using WindowsCustomizer.Core.Commands;
using WindowsCustomizer.Core.Logging;
using WindowsCustomizer.Core.Models;

namespace WindowsCustomizer.Core.Engines;

// =================================================================================================
// ISO builder (spec 33). Produces a real, bootable Windows installation ISO.
// Strategies:
//   1) oscdimg (Windows ADK) — full BIOS + UEFI boot, UDF+ISO9660. Preferred, auto-detected.
//   2) Managed fallback (DiscUtils) — ISO9660/Joliet + El Torito BIOS boot. Works for BIOS/legacy
//      and many VMs; UEFI boot needs oscdimg (clearly reported in the build report).
// The source ISO's file tree is copied first, install.wim replaced, Autounattend.xml added.
// =================================================================================================

public sealed record IsoBuildResult(
    bool Success,
    string OutputPath,
    string Strategy,
    IReadOnlyList<string> Warnings,
    long SizeBytes);

public sealed class IsoBuilder
{
    private readonly IProcessRunner _runner;
    private readonly IAppLogger _log;

    public IsoBuilder(IProcessRunner runner, IAppLogger log)
    {
        _runner = runner;
        _log = log;
    }

    /// <summary>Finds oscdimg.exe (PATH, ADK installs, custom setting).</summary>
    public static string? FindOscdimg()
    {
        var candidates = new List<string>();

        if (Environment.GetEnvironmentVariable("WC_OSCDIMG") is { Length: > 0 } custom)
        {
            candidates.Add(custom);
        }

        var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        candidates.AddRange(pathDirs.Select(d => Path.Combine(d, "oscdimg.exe")));

        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
                 })
        {
            if (string.IsNullOrEmpty(root))
            {
                continue;
            }

            var adk = Path.Combine(root, "Windows Kits", "10", "Assessment and Deployment Kit", "Deployment Tools");
            if (Directory.Exists(adk))
            {
                candidates.AddRange(Directory.GetFiles(adk, "oscdimg.exe", SearchOption.AllDirectories));
            }
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// Builds the output ISO from a prepared content directory.
    /// <paramref name="contentDir"/> must contain the full ISO file tree (boot files included).
    /// </summary>
    public async Task<IsoBuildResult> BuildAsync(
        string contentDir,
        string outputIsoPath,
        string volumeLabel,
        CancellationToken ct = default)
    {
        var warnings = new List<string>();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputIsoPath))!);

        var etfsboot = Path.Combine(contentDir, "boot", "etfsboot.com");
        var efisys = Path.Combine(contentDir, "efi", "microsoft", "boot", "efisys.bin");

        var oscdimg = FindOscdimg();
        if (oscdimg is not null)
        {
            _log.Info("IsoBuilder", $"Using oscdimg: {oscdimg}");
            var ok = await BuildWithOscdimgAsync(oscdimg, contentDir, outputIsoPath, volumeLabel, etfsboot, efisys, ct).ConfigureAwait(false);
            if (ok)
            {
                return Result(outputIsoPath, "oscdimg (BIOS+UEFI, UDF+ISO9660)", warnings);
            }

            warnings.Add("oscdimg failed — falling back to the managed ISO builder.");
        }
        else
        {
            warnings.Add("oscdimg.exe not found (install Windows ADK for full UEFI boot support). Using the managed builder.");
        }

        await Task.Run(() => BuildManaged(contentDir, outputIsoPath, volumeLabel, etfsboot), ct).ConfigureAwait(false);
        warnings.Add("Managed builder produces ISO9660/Joliet media with BIOS (El Torito) boot. For UEFI boot on bare metal, install the Windows ADK and rebuild.");
        return Result(outputIsoPath, "managed (BIOS El Torito, ISO9660/Joliet)", warnings);
    }

    private async Task<bool> BuildWithOscdimgAsync(
        string oscdimg, string contentDir, string outputIsoPath, string volumeLabel,
        string etfsboot, string efisys, CancellationToken ct)
    {
        // Standard Microsoft bootdata: 2 boot entries — BIOS (etfsboot.com) + UEFI (efisys.bin).
        var bootData = File.Exists(efisys)
            ? $"2#p0,e,b\"{etfsboot}\"#pEF,e,b\"{efisys}\""
            : $"1#p0,e,b\"{etfsboot}\"";

        var args = new List<string>
        {
            "-m", "-o", "-u2", "-udfver102",
            $"-l{TruncateLabel(volumeLabel)}",
            $"-bootdata:{bootData}",
            contentDir,
            outputIsoPath
        };

        var result = await _runner.RunAsync(
            new CommandRequest(oscdimg, args, Timeout: TimeSpan.FromHours(2)), null, ct).ConfigureAwait(false);
        return result.Success && File.Exists(outputIsoPath);
    }

    private void BuildManaged(string contentDir, string outputIsoPath, string volumeLabel, string etfsboot)
    {
        var builder = new DiscUtils.Iso9660.CDBuilder
        {
            UseJoliet = true,
            VolumeIdentifier = TruncateLabel(volumeLabel)
        };

        AddTree(builder, contentDir, string.Empty);

        if (File.Exists(etfsboot))
        {
            // El Torito "no emulation" BIOS boot image — lets legacy BIOS boot the setup.
            using var bootStream = File.OpenRead(etfsboot);
            builder.SetBootImage(bootStream, DiscUtils.Iso9660.BootDeviceEmulation.NoEmulation, 0x7C0);
        }

        builder.Build(outputIsoPath);
        _log.Success("IsoBuilder", $"Managed ISO written: {outputIsoPath}");
    }

    private static void AddTree(DiscUtils.Iso9660.CDBuilder builder, string root, string prefix)
    {
        foreach (var dir in Directory.GetDirectories(root))
        {
            var name = Path.GetFileName(dir);
            AddTree(builder, dir, prefix + name + "\\");
        }

        foreach (var file in Directory.GetFiles(root))
        {
            var name = Path.GetFileName(file);
            builder.AddFile(prefix + name, file);
        }
    }

    private static string TruncateLabel(string label)
    {
        var clean = new string(label.Where(c => c != '"' && c != '\\' && c != '/' && c != ':' && c != '*' && c != '?' && c != '<' && c != '>' && c != '|').ToArray()).Trim();
        return clean.Length == 0 ? "WIN_CUSTOM" : clean.Length <= 32 ? clean : clean[..32];
    }

    private static IsoBuildResult Result(string path, string strategy, List<string> warnings)
    {
        var size = File.Exists(path) ? new FileInfo(path).Length : 0;
        return new IsoBuildResult(size > 0, path, strategy, warnings, size);
    }
}
