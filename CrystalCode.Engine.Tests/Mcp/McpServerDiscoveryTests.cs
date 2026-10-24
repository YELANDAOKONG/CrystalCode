using CrystalCode.Engine.Home;
using CrystalCode.Engine.Mcp;
using CrystalCode.Engine.Tests.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Mcp;

public sealed class McpServerDiscoveryTests
{
    [Fact]
    public void Collect_ReturnsNothing_WhenNoConfigurationExists()
    {
        using var root = new TemporaryWorkspace();
        var discovery = Discovery(root);
        var notes = new List<string>();

        var servers = discovery.Collect(Workspace(root), notes);

        Assert.Empty(servers);
        Assert.Empty(notes);
    }

    [Fact]
    public void Collect_ReadsHomeFileAndHomeDirectory_InNameOrder()
    {
        using var root = new TemporaryWorkspace();
        WriteHomeConfig(root, """{"mcpServers":{"zeta":{"command":"z"}}}""");
        WriteServerDirectory(Path.Combine(root.Path, "home", "mcp"), "alpha", """{"command":"a"}""");
        var notes = new List<string>();

        var servers = Discovery(root).Collect(Workspace(root), notes);

        Assert.Equal(["alpha", "zeta"], servers.Select(server => server.Name));
        Assert.All(servers, server => Assert.Equal(McpServerSource.Home, server.Source));
        Assert.Empty(notes);
    }

    [Fact]
    public void Collect_ProjectReplacesHome_OnSameName()
    {
        using var root = new TemporaryWorkspace();
        WriteHomeConfig(root, """{"mcpServers":{"shared":{"command":"home-cmd","args":["a"]}}}""");
        WriteProjectConfig(root, """{"mcpServers":{"shared":{"command":"project-cmd"}}}""");

        var server = Assert.Single(Discovery(root).Collect(Workspace(root), new List<string>()));

        Assert.Equal(McpServerSource.Project, server.Source);
        Assert.Equal("project-cmd", server.Command);
        Assert.Empty(server.Arguments);
    }

    [Fact]
    public void Collect_InvalidProjectEntry_StillReplacesHome()
    {
        using var root = new TemporaryWorkspace();
        WriteHomeConfig(root, """{"mcpServers":{"shared":{"command":"home-cmd"}}}""");
        WriteProjectConfig(root, """{"mcpServers":{"shared":{"bogus":1}}}""");
        var notes = new List<string>();

        var servers = Discovery(root).Collect(Workspace(root), notes);

        Assert.Empty(servers);
        Assert.Single(notes);
    }

    [Fact]
    public void Collect_SkipsName_WhenFileAndDirectoryBothDefineIt()
    {
        using var root = new TemporaryWorkspace();
        WriteHomeConfig(root, """{"mcpServers":{"dup":{"command":"a"},"other":{"command":"o"}}}""");
        WriteServerDirectory(Path.Combine(root.Path, "home", "mcp"), "dup", """{"command":"b"}""");
        var notes = new List<string>();

        var servers = Discovery(root).Collect(Workspace(root), notes);

        Assert.Equal("other", Assert.Single(servers).Name);
        Assert.Contains("appears in both", Assert.Single(notes));
    }

    [Fact]
    public void Collect_SkipsDirectoryWithInvalidName()
    {
        using var root = new TemporaryWorkspace();
        WriteServerDirectory(Path.Combine(root.Path, "home", "mcp"), "bad__name", """{"command":"x"}""");
        var notes = new List<string>();

        var servers = Discovery(root).Collect(Workspace(root), notes);

        Assert.Empty(servers);
        Assert.Contains("name is invalid", Assert.Single(notes));
    }

    [Fact]
    public void Collect_IgnoresDirectoryWithoutServerFile()
    {
        using var root = new TemporaryWorkspace();
        Directory.CreateDirectory(Path.Combine(root.Path, "home", "mcp", "empty"));
        var notes = new List<string>();

        Assert.Empty(Discovery(root).Collect(Workspace(root), notes));
        Assert.Empty(notes);
    }

    [Fact]
    public void Collect_SkipsOversizedFile()
    {
        using var root = new TemporaryWorkspace();
        var padding = new string(' ', McpFiles.MaximumFileBytes + 1);
        WriteHomeConfig(root, """{"mcpServers":{}}""" + padding);
        var notes = new List<string>();

        Assert.Empty(Discovery(root).Collect(Workspace(root), notes));
        Assert.Contains("too large", Assert.Single(notes));
    }

    private static McpServerDiscovery Discovery(TemporaryWorkspace root) =>
        new(new CrystalHome(Path.Combine(root.Path, "home")));

    private static string Workspace(TemporaryWorkspace root)
    {
        var path = Path.Combine(root.Path, "workspace");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteHomeConfig(TemporaryWorkspace root, string json)
    {
        var home = Path.Combine(root.Path, "home");
        Directory.CreateDirectory(home);
        File.WriteAllText(Path.Combine(home, "mcp.json"), json);
    }

    private static void WriteProjectConfig(TemporaryWorkspace root, string json)
    {
        var directory = Path.Combine(Workspace(root), ".crystal");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "mcp.json"), json);
    }

    private static void WriteServerDirectory(string parent, string name, string json)
    {
        var directory = Path.Combine(parent, name);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "server.json"), json);
    }
}
