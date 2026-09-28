using System.Text.RegularExpressions;
using WindowsCustomizer.Core.Commands;
using WindowsCustomizer.Core.Logging;

namespace WindowsCustomizer.Core.Engines;

// =================================================================================================
// DISM engine (spec 6/11/12/13/14/18). Wraps dism.exe with the safe command layer. All parsing is
// separated into pure static methods so it can be unit-tested against captured DISM output without
// needing a real Windows image. Runtime operations require Windows + elevation (checked by caller).
// =================================================================================================

public sealed record DismImageInfo(int Index, string Name, string Description, long SizeBytes);

public sealed record DismPackageInfo(string PackageIdentity, string State, string ReleaseType, string InstallTime);

public sealed record DismDriverInfo(string PublishedName, string OriginalName, string ProviderName, string ClassName, string Version);

public sealed record DismFeatureInfo(string FeatureName, string State);

public sealed record DismAppxInfo(string DisplayName, string PackageName, string Version);

/// <summary>Thin, testable wrapper over DISM servicing commands.</summary>
public sealed class DismEngine
{
    private readonly IProcessRunner _runner;
    private readonly IAppLogger _log;

    public DismEngine(IProcessRunner runner, IAppLogger log)
    {
        _runner = runner;
        _log = log;
    }

    public static bool IsWindowsHost => OperatingSystem.IsWindows();

    public static string DismExe => Environment.GetEnvironmentVariable("WINDIR") is { } windir
        ? Path.Combine(windir, "System32", "dism.exe")
        : "dism.exe";

    // -------------------------------------- image listing --------------------------------------

    public async Task<List<DismImageInfo>> GetImageInfoAsync(string wimPath, CancellationToken ct = default)
    {
        var result = await _runner.RunAsync(new CommandRequest(DismExe, new[]
        {
            "/English", "/Get-WimInfo", $"/WimFile:{wimPath}"
        }), null, ct).ConfigureAwait(false);

        EnsureSuccess(result, "reading image information");
        return DismOutputParser.ParseWimInfo(result.StdOut);
    }

    // -------------------------------------- mount / unmount --------------------------------------

    public Task<CommandResult> MountImageAsync(string wimPath, int index, string mountDir, bool readOnly = false, CancellationToken ct = default)
    {
        Directory.CreateDirectory(mountDir);
        var args = new List<string>
        {
            "/English", "/Mount-Image", $"/WimFile:{wimPath}", $"/Index:{index}", $"/MountDir:{mountDir}"
        };
        if (readOnly)
        {
            args.Add("/ReadOnly");
        }

        return _runner.RunAsync(new CommandRequest(DismExe, args, Timeout: TimeSpan.FromHours(2)), null, ct);
    }

    public Task<CommandResult> UnmountImageAsync(string mountDir, bool commit, CancellationToken ct = default)
    {
        var args = new List<string>
        {
            "/English", "/Unmount-Image", $"/MountDir:{mountDir}", commit ? "/Commit" : "/Discard"
        };
        return _runner.RunAsync(new CommandRequest(DismExe, args, Timeout: TimeSpan.FromHours(2)), null, ct);
    }

    /// <summary>Recovers from a crashed session (spec: never leave a corrupt mounted image).</summary>
    public Task<CommandResult> RemountOrCleanupAsync(CancellationToken ct = default)
        => _runner.RunAsync(new CommandRequest(DismExe, new[] { "/English", "/Remount-Image", "/MountDir:C:\\wc-remount-probe" }, Timeout: TimeSpan.FromMinutes(5)), null, ct);

    public Task<CommandResult> CleanupWimAsync(CancellationToken ct = default)
        => _runner.RunAsync(new CommandRequest(DismExe, new[] { "/English", "/Cleanup-Wim" }, Timeout: TimeSpan.FromHours(1)), null, ct);

    // -------------------------------------- export / compress --------------------------------------

    public Task<CommandResult> ExportImageAsync(string srcWim, int index, string destWim, string compress = "max", CancellationToken ct = default)
    {
        var args = new List<string>
        {
            "/English", "/Export-Image", $"/SourceImageFile:{srcWim}", $"/SourceIndex:{index}",
            $"/DestinationImageFile:{destWim}", $"/Compress:{compress}", "/CheckIntegrity"
        };
        return _runner.RunAsync(new CommandRequest(DismExe, args, Timeout: TimeSpan.FromHours(4)), null, ct);
    }

    /// <summary>ESD (LZMS) -> WIM (LZX/max) — required before mounting (spec 6).</summary>
    public Task<CommandResult> ConvertEsdToWimAsync(string esdPath, string wimPath, int index, CancellationToken ct = default)
        => ExportImageAsync(esdPath, index, wimPath, "max", ct);

    public Task<CommandResult> SplitWimAsync(string wimPath, string swmDir, long partSizeMb = 3800, CancellationToken ct = default)
    {
        var pattern = Path.Combine(swmDir, Path.GetFileNameWithoutExtension(wimPath) + "*.swm");
        var args = new List<string>
        {
            "/English", "/Split-Image", $"/ImageFile:{wimPath}", $"/SWMFile:{pattern}", $"/FileSize:{partSizeMb}"
        };
        return _runner.RunAsync(new CommandRequest(DismExe, args, Timeout: TimeSpan.FromHours(2)), null, ct);
    }

    // -------------------------------------- drivers (spec 11) --------------------------------------

    public Task<CommandResult> AddDriverAsync(string mountDir, string driverPath, bool recurse, bool forceUnsigned, CancellationToken ct = default)
    {
        var args = new List<string>
        {
            "/English", "/Image:" + mountDir, "/Add-Driver", $"/Driver:{driverPath}"
        };
        if (recurse)
        {
            args.Add("/Recurse");
        }

        if (forceUnsigned)
        {
            args.Add("/ForceUnsigned");
        }

        return _runner.RunAsync(new CommandRequest(DismExe, args, Timeout: TimeSpan.FromHours(2)), null, ct);
    }

    public async Task<List<DismDriverInfo>> GetDriversAsync(string mountDir, CancellationToken ct = default)
    {
        var result = await _runner.RunAsync(new CommandRequest(DismExe, new[]
        {
            "/English", $"/Image:{mountDir}", "/Get-Drivers", "/All"
        }), null, ct).ConfigureAwait(false);
        EnsureSuccess(result, "listing drivers");
        return DismOutputParser.ParseDrivers(result.StdOut);
    }

    public Task<CommandResult> RemoveDriverAsync(string mountDir, string publishedName, CancellationToken ct = default)
        => _runner.RunAsync(new CommandRequest(DismExe, new[]
        {
            "/English", $"/Image:{mountDir}", "/Remove-Driver", $"/Driver:{publishedName}"
        }, Timeout: TimeSpan.FromHours(1)), null, ct);

    // -------------------------------------- packages / updates (spec 12/18) --------------------------------------

    public Task<CommandResult> AddPackageAsync(string mountDir, string packagePath, bool ignoreCheck, CancellationToken ct = default)
    {
        var args = new List<string>
        {
            "/English", $"/Image:{mountDir}", "/Add-Package", $"/PackagePath:{packagePath}"
        };
        if (ignoreCheck)
        {
            args.Add("/IgnoreCheck");
        }

        return _runner.RunAsync(new CommandRequest(DismExe, args, Timeout: TimeSpan.FromHours(3)), null, ct);
    }

    public async Task<List<DismPackageInfo>> GetPackagesAsync(string mountDir, CancellationToken ct = default)
    {
        var result = await _runner.RunAsync(new CommandRequest(DismExe, new[]
        {
            "/English", $"/Image:{mountDir}", "/Get-Packages"
        }), null, ct).ConfigureAwait(false);
        EnsureSuccess(result, "listing packages");
        return DismOutputParser.ParsePackages(result.StdOut);
    }

    public Task<CommandResult> RemovePackageAsync(string mountDir, string packageIdentity, CancellationToken ct = default)
        => _runner.RunAsync(new CommandRequest(DismExe, new[]
        {
            "/English", $"/Image:{mountDir}", "/Remove-Package", $"/PackageName:{packageIdentity}"
        }, Timeout: TimeSpan.FromHours(2)), null, ct);

    // -------------------------------------- features (spec 13) --------------------------------------

    public async Task<List<DismFeatureInfo>> GetFeaturesAsync(string mountDir, CancellationToken ct = default)
    {
        var result = await _runner.RunAsync(new CommandRequest(DismExe, new[]
        {
            "/English", $"/Image:{mountDir}", "/Get-Features"
        }), null, ct).ConfigureAwait(false);
        EnsureSuccess(result, "listing features");
        return DismOutputParser.ParseFeatures(result.StdOut);
    }

    public Task<CommandResult> SetFeatureAsync(string mountDir, string featureName, bool enable, CancellationToken ct = default)
        => _runner.RunAsync(new CommandRequest(DismExe, new[]
        {
            "/English", $"/Image:{mountDir}", enable ? "/Enable-Feature" : "/Disable-Feature",
            $"/FeatureName:{featureName}", "/All", "/NoRestart"
        }, Timeout: TimeSpan.FromHours(1)), null, ct);

    // -------------------------------------- provisioned apps (spec 14) --------------------------------------

    public async Task<List<DismAppxInfo>> GetProvisionedAppsAsync(string mountDir, CancellationToken ct = default)
    {
        var result = await _runner.RunAsync(new CommandRequest(DismExe, new[]
        {
            "/English", $"/Image:{mountDir}", "/Get-ProvisionedAppxPackages"
        }), null, ct).ConfigureAwait(false);
        EnsureSuccess(result, "listing provisioned apps");
        return DismOutputParser.ParseAppx(result.StdOut);
    }

    public Task<CommandResult> RemoveProvisionedAppAsync(string mountDir, string packageName, CancellationToken ct = default)
        => _runner.RunAsync(new CommandRequest(DismExe, new[]
        {
            "/English", $"/Image:{mountDir}", "/Remove-ProvisionedAppxPackage", $"/PackageName:{packageName}"
        }, Timeout: TimeSpan.FromHours(1)), null, ct);

    /// <summary>Adds an .appx/.msix (or bundle) to the image's provisioned packages (installed for every new user).</summary>
    public Task<CommandResult> AddProvisionedAppxAsync(string mountDir, string packagePath, CancellationToken ct = default)
        => _runner.RunAsync(new CommandRequest(DismExe, new[]
        {
            "/English", $"/Image:{mountDir}", "/Add-ProvisionedAppxPackage", $"/PackagePath:{packagePath}", "/SkipLicense"
        }, Timeout: TimeSpan.FromHours(1)), null, ct);

    // -------------------------------------- cleanup (spec 32) --------------------------------------

    public Task<CommandResult> CleanupImageAsync(string mountDir, CancellationToken ct = default)
        => _runner.RunAsync(new CommandRequest(DismExe, new[]
        {
            "/English", $"/Image:{mountDir}", "/Cleanup-Image", "/StartComponentCleanup", "/ResetBase", "/NoRestart"
        }, Timeout: TimeSpan.FromHours(3)), null, ct);

    private void EnsureSuccess(CommandResult result, string operation)
    {
        if (!result.Success)
        {
            var detail = result.StdErr.Length > 0 ? result.StdErr : result.StdOut;
            throw new InvalidOperationException($"DISM failed while {operation} (exit code {result.ExitCode}).\n{Truncate(detail, 2000)}");
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "...";
}

/// <summary>
/// Pure parsers for DISM English text output. Each parser is tolerant: keys are matched
/// case-insensitively and unknown lines are ignored. Covered by fixture-based unit tests.
/// </summary>
public static class DismOutputParser
{
    public static List<DismImageInfo> ParseWimInfo(string output)
    {
        var images = new List<DismImageInfo>();
        int index = 0;
        string name = "", desc = "";
        long size = 0;
        bool inImage = false;

        void Flush()
        {
            if (inImage && index > 0)
            {
                images.Add(new DismImageInfo(index, name, desc, size));
            }

            index = 0;
            name = "";
            desc = "";
            size = 0;
            inImage = false;
        }

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            var (key, value) = SplitPair(line);
            switch (key.ToLowerInvariant())
            {
                case "index":
                    Flush();
                    inImage = true;
                    int.TryParse(value, out index);
                    break;
                case "name":
                    name = value;
                    break;
                case "description":
                    desc = value;
                    break;
                case "size":
                    size = ParseSize(value);
                    break;
            }
        }

        Flush();
        return images;
    }

    public static List<DismPackageInfo> ParsePackages(string output)
    {
        var list = new List<DismPackageInfo>();
        string id = "", state = "", type = "", time = "";

        void Flush()
        {
            if (!string.IsNullOrEmpty(id))
            {
                list.Add(new DismPackageInfo(id, state, type, time));
            }

            id = state = type = time = "";
        }

        foreach (var raw in output.Split('\n'))
        {
            var (key, value) = SplitPair(raw.Trim());
            switch (key.ToLowerInvariant())
            {
                case "package identity":
                    Flush();
                    id = value;
                    break;
                case "state":
                    state = value;
                    break;
                case "release type":
                    type = value;
                    break;
                case "install time":
                    time = value;
                    break;
            }
        }

        Flush();
        return list;
    }

    public static List<DismDriverInfo> ParseDrivers(string output)
    {
        var list = new List<DismDriverInfo>();
        string pub = "", orig = "", provider = "", cls = "", ver = "";

        void Flush()
        {
            if (!string.IsNullOrEmpty(pub))
            {
                list.Add(new DismDriverInfo(pub, orig, provider, cls, ver));
            }

            pub = orig = provider = cls = ver = "";
        }

        foreach (var raw in output.Split('\n'))
        {
            var (key, value) = SplitPair(raw.Trim());
            switch (key.ToLowerInvariant())
            {
                case "published name":
                    Flush();
                    pub = value;
                    break;
                case "original file name":
                    orig = value;
                    break;
                case "provider name":
                    provider = value;
                    break;
                case "class name":
                    cls = value;
                    break;
                case "class version":
                    ver = value;
                    break;
            }
        }

        Flush();
        return list;
    }

    public static List<DismFeatureInfo> ParseFeatures(string output)
    {
        var list = new List<DismFeatureInfo>();
        string name = "", state = "";

        void Flush()
        {
            if (!string.IsNullOrEmpty(name))
            {
                list.Add(new DismFeatureInfo(name, state));
            }

            name = state = "";
        }

        foreach (var raw in output.Split('\n'))
        {
            var (key, value) = SplitPair(raw.Trim());
            switch (key.ToLowerInvariant())
            {
                case "feature name":
                    Flush();
                    name = value;
                    break;
                case "state":
                    state = value;
                    break;
            }
        }

        Flush();
        return list;
    }

    public static List<DismAppxInfo> ParseAppx(string output)
    {
        var list = new List<DismAppxInfo>();
        string display = "", pkg = "", ver = "";

        void Flush()
        {
            if (!string.IsNullOrEmpty(pkg))
            {
                list.Add(new DismAppxInfo(display, pkg, ver));
            }

            display = pkg = ver = "";
        }

        foreach (var raw in output.Split('\n'))
        {
            var (key, value) = SplitPair(raw.Trim());
            switch (key.ToLowerInvariant())
            {
                case "display name":
                    Flush();
                    display = value;
                    break;
                case "package name":
                    pkg = value;
                    break;
                case "version":
                    ver = value;
                    break;
            }
        }

        Flush();
        return list;
    }

    /// <summary>Splits "Key : Value" (DISM style). Returns empty key for non-pair lines.</summary>
    public static (string Key, string Value) SplitPair(string line)
    {
        var m = Regex.Match(line, @"^(?<k>[^:]{2,40})\s:\s(?<v>.*)$");
        return m.Success ? (m.Groups["k"].Value.Trim(), m.Groups["v"].Value.Trim()) : (string.Empty, string.Empty);
    }

    /// <summary>Parses "15,678,123,456 bytes" style sizes.</summary>
    public static long ParseSize(string value)
    {
        var digits = new string(value.Where(c => char.IsDigit(c)).ToArray());
        return long.TryParse(digits, out var size) ? size : 0;
    }
}
