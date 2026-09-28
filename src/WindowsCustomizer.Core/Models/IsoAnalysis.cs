namespace WindowsCustomizer.Core.Models;

public enum WindowsArchitecture
{
    Unknown = 0,
    X86 = 1,
    X64 = 2,
    Arm64 = 3
}

public enum BootFileKind
{
    Unknown = 0,
    /// <summary>bootmgr — BIOS/legacy boot manager.</summary>
    BiosBootManager,
    /// <summary>boot/etfsboot.com — El Torito BIOS boot sector image.</summary>
    BiosBootSector,
    /// <summary>efi/boot/bootx64.efi — UEFI x64 boot application.</summary>
    EfiX64,
    /// <summary>efi/boot/bootia32.efi — UEFI x86 boot application.</summary>
    EfiX86,
    /// <summary>efi/boot/bootaa64.efi — UEFI ARM64 boot application.</summary>
    EfiArm64,
    /// <summary>setup.exe — Windows Setup entry point.</summary>
    SetupEntry
}

public enum InstallImageFormat
{
    None = 0,
    /// <summary>install.wim — serviceable Windows image.</summary>
    Wim,
    /// <summary>install.esd — solid-compressed (recovery) image; requires export before mounting.</summary>
    Esd,
    /// <summary>install.swm — split image part.</summary>
    Swm
}

/// <summary>Summary of a WIM/ESD header (readable without full image parsing).</summary>
public sealed record WimHeaderSummary(
    int ImageCount,
    string Compression,
    string Version,
    bool IsSpanned,
    ushort PartNumber,
    ushort TotalParts,
    uint Flags,
    Guid WimGuid);

/// <summary>One file or directory discovered inside the ISO.</summary>
public sealed class IsoFileEntry
{
    /// <summary>Path using backslashes, e.g. "\\sources\\install.wim".</summary>
    public string Path { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public bool IsDirectory { get; set; }
}

public sealed class BootFileInfo
{
    public string Path { get; set; } = string.Empty;

    public BootFileKind Kind { get; set; }
}

/// <summary>Result of a read-only inspection of a Windows installation ISO.</summary>
public sealed class IsoAnalysisResult
{
    public string IsoPath { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    public string VolumeLabel { get; set; } = string.Empty;
    /// <summary>Detected volume file systems, e.g. "UDF", "ISO9660", "UDF+ISO9660".</summary>
    public string FileSystem { get; set; } = string.Empty;

    public List<IsoFileEntry> RootEntries { get; } = new();

    public List<IsoFileEntry> SourcesFiles { get; } = new();

    public List<BootFileInfo> BootFiles { get; } = new();

    public List<WindowsArchitecture> Architectures { get; } = new();

    /// <summary>Primary payload: sources/install.wim or sources/install.esd.</summary>
    public InstallImageFormat InstallImageFormat { get; set; } = InstallImageFormat.None;

    public string? InstallImagePath { get; set; }

    public long InstallImageSize { get; set; }

    public WimHeaderSummary? InstallImageHeader { get; set; }

    /// <summary>Windows Setup boot image (sources/boot.wim) — used by setup, not by end users.</summary>
    public InstallImageFormat BootImageFormat { get; set; } = InstallImageFormat.None;

    public string? BootImagePath { get; set; }

    public WimHeaderSummary? BootImageHeader { get; set; }

    /// <summary>Human-readable observations (e.g. "Windows 10/11 family installer detected").</summary>
    public List<string> Hints { get; } = new();

    /// <summary>Edition names read from the WIM XML manifest (empty when unreadable).</summary>
    public List<string> EditionNames { get; set; } = new();

    public List<string> Warnings { get; } = new();

    public List<string> Errors { get; } = new();

    public TimeSpan Elapsed { get; set; }

    public DateTimeOffset AnalyzedUtc { get; set; }

    public bool Success => Errors.Count == 0;
}

/// <summary>Progress notification for long analysis operations.</summary>
public sealed record AnalysisProgress(string Stage, double Percent, string? Detail = null);
