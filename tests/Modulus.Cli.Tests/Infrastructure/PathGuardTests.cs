using Modulus.Cli.Infrastructure;
using Shouldly;
using Xunit;

namespace Modulus.Cli.Tests.Infrastructure;

public class PathGuardTests
{
    [Fact]
    public void EnsureContained_RelativePathInsideBase_ReturnsFullPath()
    {
        var baseDir = Path.GetTempPath();

        var result = PathGuard.EnsureContained(baseDir, Path.Combine("sub", "file.txt"));

        result.ShouldStartWith(Path.GetFullPath(baseDir));
        result.ShouldEndWith("file.txt");
    }

    [Fact]
    public void EnsureContained_ParentTraversal_Throws()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "modulus-pg-test");

        Should.Throw<InvalidOperationException>(() =>
            PathGuard.EnsureContained(baseDir, Path.Combine("..", "..", "escape.txt")));
    }

    [Fact]
    public void EnsureContained_AbsolutePathOutsideBase_Throws()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "modulus-pg-test");
        var escape = OperatingSystem.IsWindows()
            ? @"C:\Windows\System32\drivers\etc\hosts"
            : "/etc/passwd";

        Should.Throw<InvalidOperationException>(() =>
            PathGuard.EnsureContained(baseDir, escape));
    }

    [Fact]
    public void EnsureContained_TrailingSeparator_DoesNotMatter()
    {
        var baseWithSep = Path.GetTempPath();
        var baseWithoutSep = baseWithSep.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var resultWith = PathGuard.EnsureContained(baseWithSep, "file.txt");
        var resultWithout = PathGuard.EnsureContained(baseWithoutSep, "file.txt");

        resultWith.ShouldBe(resultWithout);
    }

    [Fact]
    public void EnsureContained_SiblingDirectoryWithSamePrefix_Throws()
    {
        // Guards against the classic /base vs /base-evil prefix bypass.
        var baseDir = Path.Combine(Path.GetTempPath(), "base");
        var siblingPath = Path.Combine("..", "base-evil", "file.txt");

        Should.Throw<InvalidOperationException>(() =>
            PathGuard.EnsureContained(baseDir, siblingPath));
    }

    [Fact]
    public void EnsureContained_OnLinux_RejectsCaseVariantSibling()
    {
        // On case-sensitive filesystems (ext4 etc.), /home/user/basename and /home/user/BaseName
        // are distinct directories. The guard must not treat them as the same.
        if (!OperatingSystem.IsLinux())
            return;

        var baseDir = Path.Combine(Path.GetTempPath(), "basename");
        var caseVariant = Path.Combine("..", "BaseName", "file.txt");

        Should.Throw<InvalidOperationException>(() =>
            PathGuard.EnsureContained(baseDir, caseVariant));
    }

    [Fact]
    public void EnsureContained_OnWindows_AcceptsCaseVariantSameDirectory()
    {
        // On Windows / macOS the filesystem is case-insensitive by default, so the
        // guard must allow `BaseName/file.txt` when the base is `.../basename`.
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
            return;

        var baseDir = Path.Combine(Path.GetTempPath(), "basename");
        var caseVariant = Path.Combine("..", "BaseName", "file.txt");

        var result = PathGuard.EnsureContained(baseDir, caseVariant);
        result.ShouldNotBeNullOrEmpty();
    }
}
