using CrystalCode.Engine.Home;
using CrystalCode.Engine.Tests.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Home;

public sealed class ExtensionDataPathsTests
{
    [Fact]
    public void CrystalHome_ExposesDataDirectory()
    {
        using var home = new TemporaryHome();

        Assert.Equal(Path.Combine(home.Home.Root, "data"), home.Home.DataDirectory);
    }

    [Fact]
    public void Resolve_BuildsGlobalAndProjectPaths()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();

        var paths = ExtensionDataPaths.Resolve(
            home.Home,
            workspace.Path,
            ExtensionDataKind.Tools,
            "Acme.Tools");

        Assert.Equal(
            Path.Combine(home.Home.DataDirectory, "tools", "Acme.Tools"),
            paths.GlobalDirectory);
        Assert.Equal(
            Path.Combine(workspace.Path, ".crystal", "data", "tools", "Acme.Tools"),
            paths.ProjectDirectory);
    }

    [Fact]
    public void Resolve_UsesPluginsKind()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();

        var paths = ExtensionDataPaths.Resolve(
            home.Home,
            workspace.Path,
            ExtensionDataKind.Plugins,
            "acme");

        Assert.EndsWith(Path.Combine("data", "plugins", "acme"), paths.GlobalDirectory);
        Assert.EndsWith(Path.Combine(".crystal", "data", "plugins", "acme"), paths.ProjectDirectory);
    }

    [Fact]
    public void EnsureCreated_CreatesBothDirectories()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var paths = ExtensionDataPaths.Resolve(
            home.Home,
            workspace.Path,
            ExtensionDataKind.Tools,
            "acme");

        Assert.False(Directory.Exists(paths.GlobalDirectory));
        Assert.False(Directory.Exists(paths.ProjectDirectory));

        Assert.True(paths.EnsureCreated());
        Assert.True(Directory.Exists(paths.GlobalDirectory));
        Assert.True(Directory.Exists(paths.ProjectDirectory));

        Assert.True(paths.EnsureCreated());
    }

    [Fact]
    public void Resolve_RejectsMissingInputs()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();

        Assert.Throws<ArgumentNullException>(
            () => ExtensionDataPaths.Resolve(null!, workspace.Path, ExtensionDataKind.Tools, "a"));
        Assert.Throws<ArgumentException>(
            () => ExtensionDataPaths.Resolve(home.Home, " ", ExtensionDataKind.Tools, "a"));
        Assert.Throws<ArgumentException>(
            () => ExtensionDataPaths.Resolve(home.Home, workspace.Path, ExtensionDataKind.Tools, " "));
    }

    [Theory]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("../escape")]
    [InlineData("9tool")]
    [InlineData("_tool")]
    [InlineData("-tool")]
    [InlineData("a*b")]
    [InlineData("tool name")]
    [InlineData("café")]
    public void Resolve_RejectsInvalidDirectoryNames(string directoryName)
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();

        Assert.Throws<ArgumentException>(
            () => ExtensionDataPaths.Resolve(
                home.Home,
                workspace.Path,
                ExtensionDataKind.Tools,
                directoryName));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("Acme.Tools-2_x")]
    [InlineData("tool.set")]
    public void Resolve_AcceptsConformingDirectoryNames(string directoryName)
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();

        var paths = ExtensionDataPaths.Resolve(
            home.Home,
            workspace.Path,
            ExtensionDataKind.Tools,
            directoryName);

        Assert.EndsWith(directoryName, paths.GlobalDirectory);
        Assert.EndsWith(directoryName, paths.ProjectDirectory);
    }

    [Fact]
    public void Resolve_EnforcesDirectoryNameLength()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var longest = new string('a', 64);

        var paths = ExtensionDataPaths.Resolve(
            home.Home,
            workspace.Path,
            ExtensionDataKind.Tools,
            longest);

        Assert.EndsWith(longest, paths.GlobalDirectory);
        Assert.Throws<ArgumentException>(
            () => ExtensionDataPaths.Resolve(
                home.Home,
                workspace.Path,
                ExtensionDataKind.Tools,
                new string('a', 65)));
    }
}
