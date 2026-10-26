using System.Text.Json;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Tests.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Home;

public sealed class AtomicFileTests
{
    [Fact]
    public void WriteText_ReplacesContentsAndLeavesNoTemporaryFile()
    {
        using var workspace = new TemporaryWorkspace();
        var path = Path.Combine(workspace.Path, "config.json");
        File.WriteAllText(path, "{\"old\":true}");

        AtomicFile.WriteText(path, "{\"new\":true}", ownerOnly: false);

        Assert.Equal("{\"new\":true}", File.ReadAllText(path));
        Assert.Equal([path], Directory.GetFiles(workspace.Path));
    }

    [Fact]
    public void WriteText_OwnerOnlyCreatesTheFileWithOwnerAccess()
    {
        using var workspace = new TemporaryWorkspace();
        var path = Path.Combine(workspace.Path, "secret.json");

        AtomicFile.WriteText(path, "{}", ownerOnly: true);

        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        }
    }

    [Fact]
    public void WriteText_KeepsTheModeOfAReplacedFile()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var workspace = new TemporaryWorkspace();
        var path = Path.Combine(workspace.Path, "mcp.json");
        File.WriteAllText(path, "{}");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        AtomicFile.WriteText(path, "{\"enabled\":true}", ownerOnly: false);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
    }

    [Fact]
    public void WriteText_FollowLinkWritesThroughASymbolicLink()
    {
        using var workspace = new TemporaryWorkspace();
        var target = Path.Combine(workspace.Path, "dotfiles", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, "{}");
        var link = Path.Combine(workspace.Path, "config.json");
        File.CreateSymbolicLink(link, target);

        AtomicFile.WriteText(link, "{\"linked\":true}", ownerOnly: false, followLink: true);

        Assert.NotNull(new FileInfo(link).LinkTarget);
        Assert.Equal("{\"linked\":true}", File.ReadAllText(target));
    }

    [Fact]
    public void WriteText_WithoutFollowLinkReplacesTheLinkAndLeavesItsTarget()
    {
        using var workspace = new TemporaryWorkspace();
        var outside = Path.Combine(workspace.Path, "outside.txt");
        File.WriteAllText(outside, "untouched");
        var link = Path.Combine(workspace.Path, "session.json");
        File.CreateSymbolicLink(link, outside);

        AtomicFile.WriteText(link, "{}", ownerOnly: true);

        Assert.Null(new FileInfo(link).LinkTarget);
        Assert.Equal("{}", File.ReadAllText(link));
        Assert.Equal("untouched", File.ReadAllText(outside));
    }

    [Fact]
    public void WriteText_FailureLeavesThePreviousFile()
    {
        using var workspace = new TemporaryWorkspace();
        var path = Path.Combine(workspace.Path, "occupied");
        Directory.CreateDirectory(path);

        Assert.ThrowsAny<IOException>(() => AtomicFile.WriteText(path, "{}", ownerOnly: false));

        Assert.True(Directory.Exists(path));
        Assert.Equal([path], Directory.GetFileSystemEntries(workspace.Path));
    }

    [Fact]
    public void Stores_WriteValidJsonThatSurvivesRepeatedReplacement()
    {
        using var home = new TemporaryHome();
        var settings = new DiskSettingsStore(home.Home);
        var loaded = settings.LoadOrCreate();
        settings.Save(loaded);
        settings.Save(loaded);
        var grants = new GrantStore(home.Home);
        var call = new Crystal.Tools.ToolCall("call", "bash", "{\"command\":\"ls\"}", Crystal.Tools.ToolInputKind.Json);
        grants.Remember(home.Root, call, GrantScope.Persistent);

        using var config = JsonDocument.Parse(File.ReadAllText(home.Home.ConfigPath));
        using var permissions = JsonDocument.Parse(File.ReadAllText(home.Home.PermissionsPath));
        Assert.Empty(Directory.GetFiles(home.Root, "*.tmp"));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(home.Home.PermissionsPath));
        }
    }
}
