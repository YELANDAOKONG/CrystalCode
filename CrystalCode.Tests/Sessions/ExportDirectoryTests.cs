using CrystalCode.Home;
using CrystalCode.Sessions;
using CrystalCode.Tests.Home;

using Xunit;

namespace CrystalCode.Tests.Sessions;

public sealed class ExportDirectoryTests
{
    [Fact]
    public void Resolve_UsesHomeExportsByDefault()
    {
        using var root = new TemporaryHome();

        var path = ExportDirectory.Resolve(null, root.Home, "/tmp/workspace");

        Assert.Equal(Path.Combine(root.Home.Root, "exports"), path);
    }

    [Fact]
    public void Resolve_UsesHomeKeyword()
    {
        using var root = new TemporaryHome();

        var path = ExportDirectory.Resolve("home", root.Home, "/tmp/workspace");

        Assert.Equal(Path.Combine(root.Home.Root, "exports"), path);
    }

    [Fact]
    public void Resolve_UsesWorkspaceKeyword()
    {
        using var root = new TemporaryHome();

        var path = ExportDirectory.Resolve(
            "workspace",
            root.Home,
            "/tmp/workspace");

        Assert.Equal(
            Path.Combine(Path.GetFullPath("/tmp/workspace"), ".crystal", "exports"),
            path);
    }

    [Fact]
    public void Resolve_ExpandsConfiguredAbsolutePath()
    {
        using var root = new TemporaryHome();

        var path = ExportDirectory.Resolve(
            "~/custom-exports",
            root.Home,
            "/tmp/workspace");

        Assert.Equal(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "custom-exports"),
            path);
    }

    [Fact]
    public void TryResolve_RejectsInvalidConfiguredPath()
    {
        using var root = new TemporaryHome();

        var parsed = ExportDirectory.TryResolve(
            "\0invalid",
            root.Home,
            "/tmp/workspace",
            out _,
            out var error);

        Assert.False(parsed);
        Assert.StartsWith("Export directory is not valid", error, StringComparison.Ordinal);
    }
}
