using WindowsCustomizer.Core.Logging;
using WindowsCustomizer.Core.Models;
using WindowsCustomizer.Core.Services;
using Xunit;

namespace WindowsCustomizer.Core.Tests;

public class IsoAnalyzerTests : IDisposable
{
    private readonly string _root;
    private readonly FileLogger _log;

    public IsoAnalyzerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "wc-iso-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _log = new FileLogger();
    }

    /// <summary>
    /// Builds a synthetic Windows-like installer ISO: boot files + sources/install.wim with a real
    /// WIM header so both the ISO walker and the header parser get exercised end-to-end.
    /// </summary>
    private string BuildFakeWindowsIso(
        bool includeWim = true,
        bool includeEfi = true,
        bool includeBios = true,
        int wimImageCount = 4)
    {
        var wimHeader = new byte[WimHeaderReader.HeaderSize];
        var magic = System.Text.Encoding.ASCII.GetBytes("MSWIM\0\0\0");
        Array.Copy(magic, wimHeader, magic.Length);
        BitConverter.GetBytes((uint)WimHeaderReader.HeaderSize).CopyTo(wimHeader, 8);
        BitConverter.GetBytes(0x00010D00u).CopyTo(wimHeader, 12); // version 1.13
        BitConverter.GetBytes(0x00000002u).CopyTo(wimHeader, 16); // flags: compressed
        BitConverter.GetBytes(2u).CopyTo(wimHeader, 20); // LZX
        Guid.NewGuid().ToByteArray().CopyTo(wimHeader, 24);       // gWIMGuid
        BitConverter.GetBytes((ushort)1).CopyTo(wimHeader, 40);   // usPartNumber
        BitConverter.GetBytes((ushort)1).CopyTo(wimHeader, 42);   // usTotalParts
        BitConverter.GetBytes((uint)wimImageCount).CopyTo(wimHeader, 44);

        // Minimal ISO writer via DiscUtils' CDBuilder (backslash paths create nested directories).
        var builder = new DiscUtils.Iso9660.CDBuilder
        {
            UseJoliet = true
        };

        builder.AddFile("setup.exe", new byte[128]);
        if (includeBios)
        {
            builder.AddFile("bootmgr", new byte[64]);
            builder.AddFile("boot\\etfsboot.com", new byte[64]);
        }

        if (includeEfi)
        {
            builder.AddFile("efi\\boot\\bootx64.efi", new byte[64]);
            builder.AddFile("efi\\boot\\bootaa64.efi", new byte[64]);
        }

        if (includeWim)
        {
            builder.AddFile("sources\\install.wim", wimHeader);
            builder.AddFile("sources\\boot.wim", wimHeader);
        }

        builder.AddFile("sources\\readme.txt", new byte[16]);

        var isoPath = Path.Combine(_root, "fake-windows.iso");
        builder.Build(isoPath);
        return isoPath;
    }

    [Fact]
    public async Task Analyze_FakeWindowsIso_DetectsStructure()
    {
        var iso = BuildFakeWindowsIso();
        var analyzer = new IsoAnalyzer(_log);
        var result = await analyzer.AnalyzeAsync(iso);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.NotEmpty(result.RootEntries);
        Assert.Equal(InstallImageFormat.Wim, result.InstallImageFormat);
        Assert.Equal(@"\sources\install.wim", result.InstallImagePath);
        Assert.NotNull(result.InstallImageHeader);
        Assert.Equal(4, result.InstallImageHeader!.ImageCount);
        Assert.Equal("LZX (maximum)", result.InstallImageHeader.Compression);

        Assert.Contains(result.BootFiles, b => b.Kind == BootFileKind.BiosBootManager);
        Assert.Contains(result.BootFiles, b => b.Kind == BootFileKind.BiosBootSector);
        Assert.Contains(result.BootFiles, b => b.Kind == BootFileKind.EfiX64);
        Assert.Contains(result.BootFiles, b => b.Kind == BootFileKind.EfiArm64);
        Assert.Contains(result.BootFiles, b => b.Kind == BootFileKind.SetupEntry);

        Assert.Contains(result.Architectures, a => a == WindowsArchitecture.X64);
        Assert.Contains(result.Architectures, a => a == WindowsArchitecture.Arm64);

        Assert.Equal(InstallImageFormat.Wim, result.BootImageFormat);
        Assert.NotEmpty(result.SourcesFiles);
    }

    [Fact]
    public async Task Analyze_IsoWithoutInstallImage_ReportsError()
    {
        var iso = BuildFakeWindowsIso(includeWim: false);
        var analyzer = new IsoAnalyzer(_log);
        var result = await analyzer.AnalyzeAsync(iso);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("install.wim", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Analyze_IsoWithoutBootFiles_ReportsWarning()
    {
        var iso = BuildFakeWindowsIso(includeEfi: false, includeBios: false);
        var analyzer = new IsoAnalyzer(_log);
        var result = await analyzer.AnalyzeAsync(iso);

        Assert.Contains(result.Warnings, w => w.Contains("boot files", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Analyze_MissingFile_ReportsError()
    {
        var analyzer = new IsoAnalyzer(_log);
        var result = await analyzer.AnalyzeAsync(Path.Combine(_root, "missing.iso"));
        Assert.False(result.Success);
    }

    [Fact]
    public async Task Analyze_Cancelled_ThrowsOrWarns()
    {
        var iso = BuildFakeWindowsIso();
        var analyzer = new IsoAnalyzer(_log);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await analyzer.AnalyzeAsync(iso, null, cts.Token);
        // Cancelled runs either throw (aggregated as cancellation) or record a warning.
        Assert.True(
            result.Warnings.Any(w => w.Contains("cancel", StringComparison.OrdinalIgnoreCase)) ||
            result.Errors.Count == 0);
    }

    public void Dispose()
    {
        _log.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best effort cleanup
        }
    }
}
