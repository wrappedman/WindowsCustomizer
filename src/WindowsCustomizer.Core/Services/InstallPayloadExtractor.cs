namespace WindowsCustomizer.Core.Services;

using System;
using System.IO;
using System.Threading;
using WindowsCustomizer.Core.Logging;
using WindowsCustomizer.Core.Models;
using WindowsCustomizer.Core.Utilities;

/// <summary>
/// Extracts sources/install.wim (or install.esd) from the source ISO into the project's Images
/// folder. The ISO is only read — the payload is the working copy used for edition listing and
/// the build pipeline. Progress is reported so multi-GB copies never look "stuck".
/// </summary>
public static class InstallPayloadExtractor
{
    /// <summary>Names of the install payload inside the ISO (in lookup order).</summary>
    public static readonly string[] PayloadNames = { "install.wim", "install.esd" };

    /// <summary>Returns the path of the extracted payload (already present or extracted now).</summary>
    public static string Ensure(ProjectModel project, IAppLogger log, IProgress<string>? progress = null)
    {
        var imagesDir = Path.Combine(project.WorkspacePath, "Images");
        Directory.CreateDirectory(imagesDir);

        if (string.IsNullOrWhiteSpace(project.Source.OriginalIsoPath) || !File.Exists(project.Source.OriginalIsoPath))
        {
            foreach (var name in PayloadNames)
            {
                var existing = Path.Combine(imagesDir, name);
                if (File.Exists(existing) && new FileInfo(existing).Length > 1024)
                {
                    return existing;
                }
            }

            throw new FileNotFoundException("The source ISO was not found. Open the Source ISO page and select the ISO again.");
        }

        using var stream = new FileStream(project.Source.OriginalIsoPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        DiscUtils.DiscFileSystem fs;
        try
        {
            fs = new DiscUtils.Udf.UdfReader(stream);
        }
        catch
        {
            stream.Seek(0, SeekOrigin.Begin);
            fs = new DiscUtils.Iso9660.CDReader(stream, joliet: true, hideVersions: true);
        }

        using (fs)
        {
            foreach (var name in PayloadNames)
            {
                if (!fs.FileExists(@"\sources\" + name))
                {
                    continue;
                }

                var dest = Path.Combine(imagesDir, name);
                var sourceLength = fs.GetFileInfo(@"\sources\" + name).Length;

                // A cached copy is trusted only when its size matches the ISO entry exactly —
                // a partial copy from an interrupted run is re-extracted.
                if (File.Exists(dest) && new FileInfo(dest).Length == sourceLength)
                {
                    progress?.Report($"Working image already prepared: {name}");
                    return dest;
                }

                using (var src = fs.OpenFile(@"\sources\" + name, FileMode.Open, FileAccess.Read))
                {
                    CopyWithProgress(src, dest, sourceLength, name, progress);
                }

                log.Info("Payload", $"Extracted sources\\{name} -> {dest} ({new FileInfo(dest).Length / 1_000_000.0:0} MB)");
                return dest;
            }
        }

        throw new FileNotFoundException("The source ISO contains no sources\\install.wim or install.esd payload.");
    }

    /// <summary>Same as <see cref="Ensure"/> but runs on a background thread.</summary>
    public static Task<string> EnsureAsync(ProjectModel project, IAppLogger log, IProgress<string>? progress = null) =>
        Task.Run(() => Ensure(project, log, progress));

    /// <summary>Chunked copy with percentage progress (multi-GB files must not look frozen).
    /// Retries on sharing violations (antivirus/indexer briefly locking the file) and cleans
    /// up partial copies on failure.</summary>
    public static void CopyWithProgress(Stream src, string destPath, long totalBytes, string label, IProgress<string>? progress)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                CopyCore(src, destPath, totalBytes, label, progress);
                return;
            }
            catch (IOException ex) when (attempt < 4 && IsSharingViolation(ex))
            {
                progress?.Report($"{label}: the file is temporarily locked by another process (antivirus/indexer?) — retry {attempt}/3 in 3 s…");
                Thread.Sleep(3000);
                if (src.CanSeek)
                {
                    src.Seek(0, SeekOrigin.Begin);
                }
            }
            catch
            {
                TryDelete(destPath);
                throw;
            }
        }
    }

    private static void CopyCore(Stream src, string destPath, long totalBytes, string label, IProgress<string>? progress)
    {
        const int chunk = 8 * 1024 * 1024;
        using var dst = File.Create(destPath);
        var buffer = new byte[chunk];
        long copied = 0;
        var lastPercent = -10;
        while (true)
        {
            var read = src.Read(buffer, 0, chunk);
            if (read <= 0)
            {
                break;
            }

            dst.Write(buffer, 0, read);
            copied += read;
            if (totalBytes > 0 && progress is not null)
            {
                var percent = (int)(copied * 100 / totalBytes);
                if (percent >= lastPercent + 5) // every 5%
                {
                    lastPercent = percent;
                    progress.Report($"{label}: {percent}% ({copied / 1_000_000.0:0.0} / {totalBytes / 1_000_000.0:0.0} GB)");
                }
            }
        }

        progress?.Report($"{label}: 100% done");
    }

    private static bool IsSharingViolation(IOException ex) =>
        ex.HResult is unchecked((int)0x80070020) or unchecked((int)0x80070021); // ERROR_SHARING_VIOLATION / LOCK_VIOLATION

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // best effort
        }
    }
}
