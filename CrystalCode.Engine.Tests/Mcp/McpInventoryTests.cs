using System.Text.Json.Nodes;

using CrystalCode.Engine.Home;
using CrystalCode.Engine.Mcp;
using CrystalCode.Engine.Tests.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Mcp;

public sealed class McpInventoryTests
{
    [Fact]
    public void List_IncludesDisabledAndInvalidServers()
    {
        using var root = new TemporaryWorkspace();
        WriteHome(root, """
            {"mcpServers":{
              "on":{"command":"a"},
              "off":{"command":"b","enabled":false},
              "bad":{"bogus":1}}}
            """);

        var entries = McpInventory.List(Home(root), Workspace(root));

        Assert.Equal(["bad", "off", "on"], entries.Select(entry => entry.Name).ToArray());
        Assert.Null(entries.Single(entry => entry.Name == "bad").Enabled);
        Assert.Contains("unknown field", entries.Single(entry => entry.Name == "bad").Error, StringComparison.Ordinal);
        Assert.False(entries.Single(entry => entry.Name == "off").Enabled);
        Assert.True(entries.Single(entry => entry.Name == "on").Enabled);
    }

    [Fact]
    public void List_MarksTheHomeServerNotEffective_WhenProjectReplacesIt()
    {
        using var root = new TemporaryWorkspace();
        WriteHome(root, """{"mcpServers":{"shared":{"command":"home"}}}""");
        WriteProject(root, """{"mcpServers":{"shared":{"command":"project"}}}""");

        var entries = McpInventory.List(Home(root), Workspace(root));

        Assert.Equal(2, entries.Count);
        Assert.False(entries.Single(entry => entry.Source == McpServerSource.Home).Effective);
        Assert.True(entries.Single(entry => entry.Source == McpServerSource.Project).Effective);
    }

    [Fact]
    public void Format_NeverShowsCommandsArgumentsEnvironmentOrHeaders()
    {
        using var root = new TemporaryWorkspace();
        WriteHome(root, """
            {"mcpServers":{
              "a":{"command":"secret-command","args":["secret-arg"],"env":{"K":"secret-env"}},
              "b":{"url":"https://secret.example/mcp","headers":{"Authorization":"secret-header"}}}}
            """);

        var table = string.Join('\n', McpInventory.Format(McpInventory.List(Home(root), Workspace(root))));
        var one = string.Join('\n', McpInventory.Format(McpInventory.List(Home(root), Workspace(root))[0]));

        Assert.DoesNotContain("secret", table, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", one, StringComparison.Ordinal);
    }

    [Fact]
    public void Disable_RewritesOnlyThatEntryInMcpJson_AndKeepsTheRest()
    {
        using var root = new TemporaryWorkspace();
        WriteHome(root, """
            {"mcpServers":{
              "a":{"command":"keep-a","args":["x","y"],"toolTimeoutSeconds":30},
              "b":{"url":"https://example.test/mcp","headers":{"H":"v"}}}}
            """);

        var ok = McpInventory.TrySetEnabled(
            Home(root), Workspace(root), "a", null, enabled: false, out var entry, out var changed, out var error);

        Assert.True(ok, error);
        Assert.True(changed);
        Assert.False(entry!.Enabled);
        var written = JsonNode.Parse(File.ReadAllText(Path.Combine(root.Path, "home", "mcp.json")))!;
        Assert.False(written["mcpServers"]!["a"]!["enabled"]!.GetValue<bool>());
        Assert.Equal("keep-a", written["mcpServers"]!["a"]!["command"]!.GetValue<string>());
        Assert.Equal(30, written["mcpServers"]!["a"]!["toolTimeoutSeconds"]!.GetValue<int>());
        Assert.Equal("https://example.test/mcp", written["mcpServers"]!["b"]!["url"]!.GetValue<string>());
        Assert.Null(written["mcpServers"]!["b"]!["enabled"]);
    }

    [Fact]
    public void Disable_RewritesAServerJson()
    {
        using var root = new TemporaryWorkspace();
        var directory = Path.Combine(root.Path, "home", "mcp", "solo");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "server.json"), """{"command":"solo-cmd"}""");

        var ok = McpInventory.TrySetEnabled(
            Home(root), Workspace(root), "solo", null, enabled: false, out _, out var changed, out var error);

        Assert.True(ok, error);
        Assert.True(changed);
        var written = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "server.json")))!;
        Assert.False(written["enabled"]!.GetValue<bool>());
        Assert.Equal("solo-cmd", written["command"]!.GetValue<string>());
    }

    [Fact]
    public void Enable_ReportsNoChange_WhenAlreadyEnabled_AndLeavesTheFileAlone()
    {
        using var root = new TemporaryWorkspace();
        const string original = """{"mcpServers":{"a":{"command":"x"}}}""";
        WriteHome(root, original);

        var ok = McpInventory.TrySetEnabled(
            Home(root), Workspace(root), "a", null, enabled: true, out _, out var changed, out var error);

        Assert.True(ok, error);
        Assert.False(changed);
        Assert.Equal(original, File.ReadAllText(Path.Combine(root.Path, "home", "mcp.json")));
    }

    [Fact]
    public void Disable_DefaultsToTheProjectServer_AndSourceSelectsTheHomeOne()
    {
        using var root = new TemporaryWorkspace();
        WriteHome(root, """{"mcpServers":{"shared":{"command":"home"}}}""");
        WriteProject(root, """{"mcpServers":{"shared":{"command":"project"}}}""");

        McpInventory.TrySetEnabled(
            Home(root), Workspace(root), "shared", null, enabled: false, out var chosen, out _, out _);
        McpInventory.TrySetEnabled(
            Home(root), Workspace(root), "shared", McpServerSource.Home, enabled: false, out var home, out _, out _);

        Assert.Equal(McpServerSource.Project, chosen!.Source);
        Assert.Equal(McpServerSource.Home, home!.Source);
        Assert.Contains("\"enabled\": false", File.ReadAllText(Path.Combine(root.Path, "home", "mcp.json")), StringComparison.Ordinal);
        Assert.Contains("\"enabled\": false", File.ReadAllText(ProjectFile(root)), StringComparison.Ordinal);
    }

    [Fact]
    public void Disable_FailsForAnInvalidServer_WithoutTouchingTheFile()
    {
        using var root = new TemporaryWorkspace();
        const string original = """{"mcpServers":{"bad":{"bogus":1}}}""";
        WriteHome(root, original);

        var ok = McpInventory.TrySetEnabled(
            Home(root), Workspace(root), "bad", null, enabled: false, out _, out var changed, out var error);

        Assert.False(ok);
        Assert.False(changed);
        Assert.Contains("invalid", error, StringComparison.Ordinal);
        Assert.Equal(original, File.ReadAllText(Path.Combine(root.Path, "home", "mcp.json")));
    }

    [Fact]
    public void TryFind_FailsForAnUnknownServerAndAWrongSource()
    {
        using var root = new TemporaryWorkspace();
        WriteHome(root, """{"mcpServers":{"a":{"command":"x"}}}""");

        Assert.False(McpInventory.TryFind(Home(root), Workspace(root), "nope", null, out _, out var missing));
        Assert.False(McpInventory.TryFind(Home(root), Workspace(root), "a", McpServerSource.Project, out _, out var wrong));

        Assert.Equal("MCP server 'nope' was not found.", missing);
        Assert.Equal("MCP server 'a' was not found in Project.", wrong);
    }

    [Fact]
    public void Collect_SkipsDisabledServers_ButKeepsThemInTheInventory()
    {
        using var root = new TemporaryWorkspace();
        WriteHome(root, """{"mcpServers":{"on":{"command":"a"},"off":{"command":"b","enabled":false}}}""");

        var connected = new McpServerDiscovery(Home(root)).Collect(Workspace(root), new List<string>());

        Assert.Equal(["on"], connected.Select(server => server.Name).ToArray());
        Assert.Equal(2, McpInventory.List(Home(root), Workspace(root)).Count);
    }

    [Fact]
    public void Collect_ADisabledProjectServerStillReplacesTheHomeOne()
    {
        using var root = new TemporaryWorkspace();
        WriteHome(root, """{"mcpServers":{"shared":{"command":"home"}}}""");
        WriteProject(root, """{"mcpServers":{"shared":{"command":"project","enabled":false}}}""");

        var connected = new McpServerDiscovery(Home(root)).Collect(Workspace(root), new List<string>());

        Assert.Empty(connected);
    }

    [Theory]
    [InlineData("""{"mcpServers":{"a":{"command":"x","enabled":"no"}}}""")]
    [InlineData("""{"mcpServers":{"a":{"command":"x","enabled":1}}}""")]
    public void Parse_RejectsANonBooleanEnabled(string json)
    {
        var notes = new List<string>();

        var entries = McpConfigParser.ParseFile(json, McpServerSource.Home, "mcp.json", notes);

        Assert.Null(Assert.Single(entries).Definition);
        Assert.Contains("'enabled' must be true or false", Assert.Single(notes), StringComparison.Ordinal);
    }

    private static CrystalHome Home(TemporaryWorkspace root) => new(Path.Combine(root.Path, "home"));

    private static string Workspace(TemporaryWorkspace root)
    {
        var path = Path.Combine(root.Path, "workspace");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ProjectFile(TemporaryWorkspace root) =>
        Path.Combine(Workspace(root), ".crystal", "mcp.json");

    private static void WriteHome(TemporaryWorkspace root, string json)
    {
        Directory.CreateDirectory(Path.Combine(root.Path, "home"));
        File.WriteAllText(Path.Combine(root.Path, "home", "mcp.json"), json);
    }

    private static void WriteProject(TemporaryWorkspace root, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ProjectFile(root))!);
        File.WriteAllText(ProjectFile(root), json);
    }
}
