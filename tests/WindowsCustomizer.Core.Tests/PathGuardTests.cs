using WindowsCustomizer.Core.Utilities;
using Xunit;

namespace WindowsCustomizer.Core.Tests;

public class PathGuardTests
{
    [Fact]
    public void IsInsideDirectory_ChildPath_ReturnsTrue()
    {
        var root = Path.Combine(Path.GetTempPath(), "wc-test-root");
        var child = Path.Combine(root, "sub", "file.txt");
        Assert.True(PathGuard.IsInsideDirectory(root, child));
    }

    [Fact]
    public void IsInsideDirectory_SiblingWithCommonPrefix_ReturnsFalse()
    {
        // "/tmp/wc-test-root-evil" must not count as inside "/tmp/wc-test-root".
        var root = Path.Combine(Path.GetTempPath(), "wc-test-root");
        var evil = Path.Combine(Path.GetTempPath(), "wc-test-root-evil", "file.txt");
        Assert.False(PathGuard.IsInsideDirectory(root, evil));
    }

    [Fact]
    public void EnsureInsideWorkspace_TraversalAttempt_Throws()
    {
        var root = Path.Combine(Path.GetTempPath(), "wc-guard");
        Directory.CreateDirectory(root);
        Assert.Throws<InvalidOperationException>(() =>
            PathGuard.EnsureInsideWorkspace(root, Path.Combine(root, "..", "escape.txt")));
    }

    [Fact]
    public void EnsureInsideWorkspace_AbsoluteEscape_Throws()
    {
        var root = Path.Combine(Path.GetTempPath(), "wc-guard");
        Directory.CreateDirectory(root);
        Assert.Throws<InvalidOperationException>(() =>
            PathGuard.EnsureInsideWorkspace(root, Path.Combine(Path.GetTempPath(), "outside.txt")));
    }

    [Fact]
    public void EnsureInsideWorkspace_InsidePath_ReturnsNormalized()
    {
        var root = Path.Combine(Path.GetTempPath(), "wc-guard");
        Directory.CreateDirectory(root);
        var result = PathGuard.EnsureInsideWorkspace(root, Path.Combine(root, "a", "b.txt"));
        Assert.StartsWith(Path.GetFullPath(root), result);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("MyProject", true)]
    [InlineData("My Project 2", true)]
    [InlineData("a/b", false)]
    [InlineData("a\\b", false)]
    [InlineData(".hidden", false)]
    [InlineData("CON", false)]
    [InlineData("nul", false)]
    [InlineData("Project.", false)]
    public void IsValidProjectName_ChecksRules(string? name, bool expected)
    {
        var ok = PathGuard.IsValidProjectName(name, out var error);
        Assert.Equal(expected, ok);
        if (!expected)
        {
            Assert.False(string.IsNullOrWhiteSpace(error));
        }
    }

    [Fact]
    public void SanitizeFileName_RemovesInvalidCharacters()
    {
        var cleaned = PathGuard.SanitizeFileName("a<b>:c|d?.txt");
        Assert.DoesNotContain("<", cleaned);
        Assert.DoesNotContain("|", cleaned);
        Assert.EndsWith("txt", cleaned);
    }

    [Fact]
    public void LooksLikeTraversal_DetectsRelativeEscapes()
    {
        Assert.True(PathGuard.LooksLikeTraversal("../x"));
        Assert.True(PathGuard.LooksLikeTraversal("a/../../b"));
        Assert.True(PathGuard.LooksLikeTraversal("/absolute"));
        Assert.False(PathGuard.LooksLikeTraversal("folder/file.txt"));
    }
}
