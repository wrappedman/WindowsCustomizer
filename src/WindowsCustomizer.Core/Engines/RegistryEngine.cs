using System.Text;
using WindowsCustomizer.Core.Commands;
using WindowsCustomizer.Core.Logging;
using WindowsCustomizer.Core.Models;
using WindowsCustomizer.Core.Utilities;

namespace WindowsCustomizer.Core.Engines;

// =================================================================================================
// Registry engine (spec 15/16/17). Two halves:
//   1) RegFileBuilder — pure, cross-platform REG (REGEDIT5) generation, fully unit-tested.
//   2) RegistryEngine — offline hive load/unload + .reg import via reg.exe (Windows runtime).
// Service startup types are also applied through registry values (Start=2/3/4).
// =================================================================================================

/// <summary>Generates REGEDIT5 (.reg) text from tweak entries.</summary>
public static class RegFileBuilder
{
    public const string Header = "Windows Registry Editor Version 5.00";

    public static string Build(IEnumerable<RegistryTweakEntry> tweaks)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Header);
        sb.AppendLine();

        foreach (var group in tweaks
                     .Where(t => t.Enabled && !string.IsNullOrWhiteSpace(t.KeyPath))
                     .GroupBy(t => (t.Hive, t.KeyPath))
                     .OrderBy(g => g.Key.Hive).ThenBy(g => g.Key.KeyPath))
        {
            sb.Append('[').Append(FormatRoot(group.Key.Hive)).Append('\\')
              .Append(EscapeKeyPath(group.Key.KeyPath)).AppendLine("]");
            foreach (var t in group)
            {
                sb.Append('"').Append(Escape(t.ValueName)).Append("\"=")
                  .Append(FormatValue(t.ValueKind, t.Data)).AppendLine();
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>Builds a single-key delete directive (used for "remove value" tweaks).</summary>
    public static string BuildDeleteValue(string hive, string keyPath, string valueName)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Header);
        sb.AppendLine();
        sb.Append('[').Append(FormatRoot(hive)).Append('\\').Append(EscapeKeyPath(keyPath)).AppendLine("]");
        sb.Append('"').Append(Escape(valueName)).AppendLine("\"=-");
        return sb.ToString();
    }

    /// <summary>Service startup mapping: Automatic=2, Delayed=2+DelayedAutostart, Manual=3, Disabled=4.</summary>
    public static IEnumerable<RegistryTweakEntry> ServiceStartTweaks(IEnumerable<ServiceEntry> services)
    {
        foreach (var s in services.Where(x => x.Enabled && x.Action == ServiceAction.Configure))
        {
            var start = s.Startup switch
            {
                StartupType.Automatic => 2,
                StartupType.AutomaticDelayed => 2,
                StartupType.Manual => 3,
                StartupType.Disabled => 4,
                _ => 3
            };

            yield return new RegistryTweakEntry
            {
                Category = "Services",
                Hive = "HKLM",
                KeyPath = @"SYSTEM\CurrentControlSet\Services\" + s.ServiceName,
                ValueName = "Start",
                ValueKind = RegistryValueKind2.Dword,
                Data = start.ToString(),
                Risk = s.IsCritical ? RiskLevel.High : RiskLevel.Medium,
                Enabled = true,
                Description = $"Set '{s.DisplayName}' startup to {s.Startup}"
            };

            if (s.Startup == StartupType.AutomaticDelayed)
            {
                yield return new RegistryTweakEntry
                {
                    Category = "Services",
                    Hive = "HKLM",
                    KeyPath = @"SYSTEM\CurrentControlSet\Services\" + s.ServiceName,
                    ValueName = "DelayedAutostart",
                    ValueKind = RegistryValueKind2.Dword,
                    Data = "1",
                    Risk = RiskLevel.Low,
                    Enabled = true,
                    Description = $"Delayed start for '{s.DisplayName}'"
                };
            }
        }
    }

    /// <summary>
    /// Full offline registration for services created by the user (custom binaries copied into the
    /// image). Produces the classic service key: Type/Start/ErrorControl/ImagePath/DisplayName/ObjectName.
    /// </summary>
    public static IEnumerable<RegistryTweakEntry> ServiceCreateTweaks(IEnumerable<ServiceEntry> services)
    {
        foreach (var s in services.Where(x => x.Enabled && x.Action == ServiceAction.Create
                                              && !string.IsNullOrWhiteSpace(x.ServiceName)))
        {
            var key = @"SYSTEM\CurrentControlSet\Services\" + s.ServiceName;
            var start = s.Startup switch
            {
                StartupType.Automatic => 2,
                StartupType.AutomaticDelayed => 2,
                StartupType.Manual => 3,
                StartupType.Disabled => 4,
                _ => 2
            };
            var imagePath = string.IsNullOrWhiteSpace(s.ImagePath) ? s.SourcePath : s.ImagePath;

            RegistryTweakEntry Make(string valueName, RegistryValueKind2 kind, string data) => new()
            {
                Category = "Services", Hive = "HKLM", KeyPath = key, ValueName = valueName,
                ValueKind = kind, Data = data, Risk = RiskLevel.Medium, Enabled = true,
                Description = $"Create service '{s.DisplayName}': {valueName}"
            };

            yield return Make("Type", RegistryValueKind2.Dword, "16"); // own process
            yield return Make("Start", RegistryValueKind2.Dword, start.ToString());
            yield return Make("ErrorControl", RegistryValueKind2.Dword, "1");
            yield return Make("ImagePath", RegistryValueKind2.ExpandString, imagePath);
            yield return Make("DisplayName", RegistryValueKind2.String,
                string.IsNullOrWhiteSpace(s.DisplayName) ? s.ServiceName : s.DisplayName);
            yield return Make("ObjectName", RegistryValueKind2.String, @"LocalSystem");
        }
    }

    /// <summary>.reg content that DELETES the service keys (unwanted services removed from the image).</summary>
    public static string ServiceDeletesReg(IEnumerable<ServiceEntry> services)
    {
        var sb = new StringBuilder();
        var any = false;
        foreach (var s in services.Where(x => x.Enabled && x.Action == ServiceAction.Delete
                                              && !string.IsNullOrWhiteSpace(x.ServiceName)))
        {
            if (!any)
            {
                sb.AppendLine(Header);
                sb.AppendLine();
                any = true;
            }

            sb.Append("[-").Append(FormatRoot("HKLM")).Append('\\')
              .Append(EscapeKeyPath(@"SYSTEM\CurrentControlSet\Services\" + s.ServiceName)).AppendLine("]");
        }

        return any ? sb.ToString() : string.Empty;
    }

    public static string FormatRoot(string hive) => hive.ToUpperInvariant() switch
    {
        "HKLM" or "HKEY_LOCAL_MACHINE" => "HKEY_LOCAL_MACHINE",
        "HKCU" or "HKEY_CURRENT_USER" => "HKEY_CURRENT_USER",
        "HKU" or "HKEY_USERS" => "HKEY_USERS",
        "HKCR" or "HKEY_CLASSES_ROOT" => "HKEY_CLASSES_ROOT",
        _ => hive
    };

    /// <summary>Hive name for reg.exe load (HKLM\Software vs ...\System).</summary>
    public static string OfflineHiveFor(string hive, string keyPath)
    {
        if (hive is "HKLM" or "HKEY_LOCAL_MACHINE")
        {
            return keyPath.StartsWith("SYSTEM", StringComparison.OrdinalIgnoreCase)
                ? "WCS_SYSTEM"
                : "WCS_SOFTWARE";
        }

        return "WCS_DEFAULT"; // HKCU default user hive
    }

    public static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    public static string EscapeKeyPath(string keyPath) => keyPath.Replace("\\", "\\\\");

    public static string FormatValue(RegistryValueKind2 kind, string data) => kind switch
    {
        RegistryValueKind2.Dword => $"dword:{ParseDword(data):x8}",
        RegistryValueKind2.Qword => $"hex(b):{FormatHexBytes(BitConverter.GetBytes(ParseQword(data)))}",
        RegistryValueKind2.Binary => $"hex:{FormatHexBytes(ParseHexBytes(data))}",
        RegistryValueKind2.ExpandString => $"hex(2):{FormatHexBytes(Encoding.Unicode.GetBytes(data + "\0"))}",
        RegistryValueKind2.MultiString => $"hex(7):{FormatHexBytes(Encoding.Unicode.GetBytes(string.Join("\0", data.Split(';')) + "\0\0"))}",
        _ => $"\"{Escape(data)}\""
    };

    private static uint ParseDword(string data) =>
        data.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToUInt32(data[2..], 16)
            : uint.TryParse(data, out var v) ? v : 0u;

    private static ulong ParseQword(string data) =>
        data.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToUInt64(data[2..], 16)
            : ulong.TryParse(data, out var v) ? v : 0ul;

    private static byte[] ParseHexBytes(string data) => data
        .Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(b => Convert.ToByte(b, 16))
        .ToArray();

    private static string FormatHexBytes(byte[] bytes) =>
        string.Join(",", bytes.Select(b => b.ToString("x2")));
}

/// <summary>
/// Offline registry operations against a mounted image. Uses reg.exe with pre-generated .reg
/// files (never raw shell strings). Hive files: SOFTWARE, SYSTEM, DEFAULT (spec 15).
/// </summary>
public sealed class RegistryEngine
{
    private readonly IProcessRunner _runner;
    private readonly IAppLogger _log;

    public RegistryEngine(IProcessRunner runner, IAppLogger log)
    {
        _runner = runner;
        _log = log;
    }

    /// <summary>Applies registry tweaks to a mounted image by loading its hives into a temp key.</summary>
    public async Task ApplyTweaksAsync(string mountDir, IReadOnlyList<RegistryTweakEntry> tweaks, string tempDir, CancellationToken ct = default)
    {
        var enabled = tweaks.Where(t => t.Enabled).ToList();
        if (enabled.Count == 0)
        {
            _log.Info("Registry", "No registry tweaks to apply.");
            return;
        }

        Directory.CreateDirectory(tempDir);
        var byHive = enabled.GroupBy(t => RegFileBuilder.OfflineHiveFor(t.Hive, t.KeyPath));

        foreach (var group in byHive)
        {
            var hiveFile = HiveFileFor(mountDir, group.Key);
            var mountPoint = @"HKLM\" + group.Key;
            ct.ThrowIfCancellationRequested();

            var load = await _runner.RunAsync(new CommandRequest("reg.exe", new[]
            {
                "load", mountPoint, hiveFile
            }, Timeout: TimeSpan.FromMinutes(5)), null, ct).ConfigureAwait(false);
            if (!load.Success)
            {
                throw new InvalidOperationException($"Failed to load offline hive '{hiveFile}' (exit {load.ExitCode}).");
            }

            try
            {
                var regFile = Path.Combine(tempDir, group.Key + ".reg");
                File.WriteAllText(regFile, RegFileBuilder.Build(group.ToList()), Encoding.Unicode);

                // regedit /s is used instead of `reg import` so we control quiet mode explicitly.
                var import = await _runner.RunAsync(new CommandRequest("regedit.exe", new[]
                {
                    "/s", regFile
                }, Timeout: TimeSpan.FromMinutes(10)), null, ct).ConfigureAwait(false);
                if (!import.Success)
                {
                    // Fallback: reg import
                    var fallback = await _runner.RunAsync(new CommandRequest("reg.exe", new[]
                    {
                        "import", regFile
                    }, Timeout: TimeSpan.FromMinutes(10)), null, ct).ConfigureAwait(false);
                    if (!fallback.Success)
                    {
                        throw new InvalidOperationException($"Failed to import registry tweaks (exit {fallback.ExitCode}).");
                    }
                }
            }
            finally
            {
                await _runner.RunAsync(new CommandRequest("reg.exe", new[]
                {
                    "unload", mountPoint
                }, Timeout: TimeSpan.FromMinutes(5)), null, ct).ConfigureAwait(false);
            }
        }

        _log.Success("Registry", $"Applied {enabled.Count} registry tweak(s) to the offline image.");
    }

    /// <summary>
    /// Imports raw .reg content (e.g. service key deletions) into one offline hive
    /// (WCS_SOFTWARE / WCS_SYSTEM / WCS_DEFAULT).
    /// </summary>
    public async Task ApplyRawRegAsync(string mountDir, string hiveName, string regContent, string tempDir, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(regContent))
        {
            return;
        }

        Directory.CreateDirectory(tempDir);
        var hiveFile = HiveFileFor(mountDir, hiveName);
        var mountPoint = @"HKLM\" + hiveName;
        var load = await _runner.RunAsync(new CommandRequest("reg.exe", new[]
        {
            "load", mountPoint, hiveFile
        }, Timeout: TimeSpan.FromMinutes(5)), null, ct).ConfigureAwait(false);
        if (!load.Success)
        {
            throw new InvalidOperationException($"Failed to load offline hive '{hiveFile}' (exit {load.ExitCode}).");
        }

        try
        {
            var regFile = Path.Combine(tempDir, hiveName + "-raw.reg");
            File.WriteAllText(regFile, regContent, Encoding.Unicode);
            var import = await _runner.RunAsync(new CommandRequest("regedit.exe", new[]
            {
                "/s", regFile
            }, Timeout: TimeSpan.FromMinutes(10)), null, ct).ConfigureAwait(false);
            if (!import.Success)
            {
                var fallback = await _runner.RunAsync(new CommandRequest("reg.exe", new[]
                {
                    "import", regFile
                }, Timeout: TimeSpan.FromMinutes(10)), null, ct).ConfigureAwait(false);
                if (!fallback.Success)
                {
                    throw new InvalidOperationException($"Failed to import raw registry content (exit {fallback.ExitCode}).");
                }
            }
        }
        finally
        {
            await _runner.RunAsync(new CommandRequest("reg.exe", new[]
            {
                "unload", mountPoint
            }, Timeout: TimeSpan.FromMinutes(5)), null, ct).ConfigureAwait(false);
        }

        _log.Success("Registry", "Raw registry content applied to the offline image.");
    }

    private static string HiveFileFor(string mountDir, string hiveName) => hiveName switch
    {
        "WCS_SOFTWARE" => Path.Combine(mountDir, "Windows", "System32", "config", "SOFTWARE"),
        "WCS_SYSTEM" => Path.Combine(mountDir, "Windows", "System32", "config", "SYSTEM"),
        _ => Path.Combine(mountDir, "Users", "Default", "NTUSER.DAT")
    };
}
