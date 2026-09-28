using System.Text.RegularExpressions;

namespace WindowsCustomizer.Core.Utilities;

/// <summary>
/// Path safety helpers (spec: Security). Every engine that touches the file system must resolve
/// destinations through these helpers so a crafted name can never escape the workspace.
/// </summary>
public static class PathGuard
{
    private static readonly char[] InvalidNameChars =
        // Windows file-name rules (the app targets Windows even when unit tests run on Linux).
        new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' }
            .Concat(Enumerable.Range(0, 32).Select(i => (char)i))
            .ToArray();

    private static readonly HashSet<string> ReservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>True when <paramref name="candidate"/> resolves to <paramref name="directory"/> or below it.</summary>
    public static bool IsInsideDirectory(string directory, string candidate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidate);

        var root = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var target = Path.GetFullPath(candidate)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(root, target, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns the normalized full path of <paramref name="candidate"/> but throws when it points
    /// outside <paramref name="workspace"/> (path traversal / absolute escape protection).
    /// </summary>
    public static string EnsureInsideWorkspace(string workspace, string candidate)
    {
        var full = Path.GetFullPath(candidate);
        if (!IsInsideDirectory(workspace, full))
        {
            throw new InvalidOperationException(
                $"Blocked path escape: '{candidate}' resolves outside of the project workspace '{workspace}'.");
        }

        return full;
    }

    /// <summary>Validates a project name (used as folder name and .wcp file name).</summary>
    public static bool IsValidProjectName(string? name, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(name))
        {
            error = "Project name is required.";
            return false;
        }

        var trimmed = name.Trim();

        if (trimmed.Length > 64)
        {
            error = "Project name must be 64 characters or fewer.";
            return false;
        }

        if (trimmed.IndexOfAny(InvalidNameChars) >= 0)
        {
            error = "Project name contains invalid characters.";
            return false;
        }

        if (trimmed.StartsWith('.') || trimmed.EndsWith('.'))
        {
            error = "Project name cannot start or end with a dot.";
            return false;
        }

        if (ReservedWindowsNames.Contains(trimmed))
        {
            error = $"'{trimmed}' is a reserved Windows name.";
            return false;
        }

        return true;
    }

    /// <summary>Removes invalid file-name characters and normalizes the result.</summary>
    public static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "file";
        }

        var invalid = new string(InvalidNameChars) + " \"':;|<>*?/\\";
        var cleaned = Regex.Replace(name, "[" + Regex.Escape(invalid) + "]", "_").Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(cleaned) ? "file" : cleaned;
    }

    /// <summary>True when the path is rooted or contains traversal segments before normalization.</summary>
    public static bool LooksLikeTraversal(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        if (Path.IsPathRooted(candidate))
        {
            return true;
        }

        return candidate.Contains("..", StringComparison.Ordinal);
    }
}
