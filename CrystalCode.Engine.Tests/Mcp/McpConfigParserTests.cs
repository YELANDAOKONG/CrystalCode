using CrystalCode.Engine.Mcp;

using Xunit;

namespace CrystalCode.Engine.Tests.Mcp;

public sealed class McpConfigParserTests
{
    private const string Origin = "mcp.json";

    [Fact]
    public void ParseFile_ReadsStdioServer()
    {
        var notes = new List<string>();

        var entries = McpConfigParser.ParseFile(
            """
            {"mcpServers":{"fs":{"command":"npx","args":["-y","server"],"env":{"TOKEN":"${TOKEN}"}}}}
            """,
            McpServerSource.Home,
            Origin,
            notes);

        var definition = Assert.Single(entries).Definition;
        Assert.NotNull(definition);
        Assert.Equal("fs", definition.Name);
        Assert.Equal(McpTransportKind.Stdio, definition.Transport);
        Assert.Equal("npx", definition.Command);
        Assert.Equal(["-y", "server"], definition.Arguments);
        Assert.Equal("${TOKEN}", definition.Environment["TOKEN"]);
        Assert.Empty(notes);
    }

    [Fact]
    public void ParseFile_ReadsHttpServer_WhenOnlyUrlIsGiven()
    {
        var notes = new List<string>();

        var entries = McpConfigParser.ParseFile(
            """
            {"mcpServers":{"remote":{"url":"https://example.test/mcp","headers":{"Authorization":"Bearer ${KEY}"}}}}
            """,
            McpServerSource.Project,
            Origin,
            notes);

        var definition = Assert.Single(entries).Definition;
        Assert.NotNull(definition);
        Assert.Equal(McpTransportKind.StreamableHttp, definition.Transport);
        Assert.Equal(McpServerSource.Project, definition.Source);
        Assert.Equal("Bearer ${KEY}", definition.Headers["authorization"]);
        Assert.Empty(notes);
    }

    [Fact]
    public void ParseFile_DefaultsToWorkCatalogAndStandardTimeout()
    {
        var entries = McpConfigParser.ParseFile(
            """{"mcpServers":{"a":{"command":"x"}}}""",
            McpServerSource.Home,
            Origin,
            new List<string>());

        var definition = Assert.Single(entries).Definition;
        Assert.NotNull(definition);
        Assert.True(definition.InWork);
        Assert.False(definition.InPlan);
        Assert.Equal(McpServerDefinition.DefaultToolTimeoutSeconds, definition.ToolTimeoutSeconds);
    }

    [Fact]
    public void ParseFile_ReadsCatalogsAndUnlimitedTimeout()
    {
        var entries = McpConfigParser.ParseFile(
            """
            {"mcpServers":{"a":{"command":"x","catalogs":["plan","work"],"toolTimeoutSeconds":"unlimited"}}}
            """,
            McpServerSource.Home,
            Origin,
            new List<string>());

        var definition = Assert.Single(entries).Definition;
        Assert.NotNull(definition);
        Assert.True(definition.InPlan);
        Assert.True(definition.InWork);
        Assert.Null(definition.ToolTimeoutSeconds);
    }

    [Theory]
    [InlineData("""{"mcpServers":{"a":{"command":"x","bogus":1}}}""", "unknown field 'bogus'")]
    [InlineData("""{"mcpServers":{"a":{}}}""", "exactly one of")]
    [InlineData("""{"mcpServers":{"a":{"command":"x","url":"https://e.test"}}}""", "exactly one of")]
    [InlineData("""{"mcpServers":{"a":{"command":"x","headers":{"k":"v"}}}}""", "cannot have 'url' or 'headers'")]
    [InlineData("""{"mcpServers":{"a":{"url":"https://e.test","args":["x"]}}}""", "cannot have 'command'")]
    [InlineData("""{"mcpServers":{"a":{"type":"websocket","url":"wss://e.test"}}}""", "not supported")]
    [InlineData("""{"mcpServers":{"a":{"command":"x","args":[1]}}}""", "array of strings")]
    [InlineData("""{"mcpServers":{"a":{"command":"x","env":{"A=B":"v"}}}}""", "plain names")]
    [InlineData("""{"mcpServers":{"a":{"command":"x","catalogs":["plan","plan"]}}}""", "without repeats")]
    [InlineData("""{"mcpServers":{"a":{"command":"x","catalogs":[]}}}""", "without repeats")]
    [InlineData("""{"mcpServers":{"a":{"command":"x","toolTimeoutSeconds":0}}}""", "positive number")]
    [InlineData("""{"mcpServers":{"bad__name":{"command":"x"}}}""", "name is invalid")]
    [InlineData("""{"mcpServers":{"a":"command"}}""", "must be an object")]
    public void ParseFile_SkipsServer_WhenDefinitionIsInvalid(string json, string expected)
    {
        var notes = new List<string>();

        var entries = McpConfigParser.ParseFile(json, McpServerSource.Home, Origin, notes);

        Assert.Null(Assert.Single(entries).Definition);
        var note = Assert.Single(notes);
        Assert.Contains(expected, note);
        Assert.Contains(Origin, note);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"other":{}}""")]
    [InlineData("""{"mcpServers":[]}""")]
    [InlineData("""{"mcpServers":{},"mcpServers":{}}""")]
    [InlineData("""{"mcpServers":{"a":{"command":"x"}},}""")]
    public void ParseFile_SkipsWholeFile_WhenShapeIsInvalid(string json)
    {
        var notes = new List<string>();

        var entries = McpConfigParser.ParseFile(json, McpServerSource.Home, Origin, notes);

        Assert.Empty(entries);
        Assert.Single(notes);
    }

    [Fact]
    public void ParseFile_SkipsBothEntries_WhenNameIsRepeated()
    {
        var notes = new List<string>();

        var entries = McpConfigParser.ParseFile(
            """{"mcpServers":{"a":{"command":"x"},"a":{"command":"y"}}}""",
            McpServerSource.Home,
            Origin,
            notes);

        Assert.Null(Assert.Single(entries).Definition);
        Assert.Contains("more than once", Assert.Single(notes));
    }

    [Fact]
    public void ParseFile_KeepsValidServers_WhenAnotherIsInvalid()
    {
        var notes = new List<string>();

        var entries = McpConfigParser.ParseFile(
            """{"mcpServers":{"good":{"command":"x"},"bad":{}}}""",
            McpServerSource.Home,
            Origin,
            notes);

        Assert.Equal(2, entries.Count);
        Assert.NotNull(entries.Single(entry => entry.Name == "good").Definition);
        Assert.Null(entries.Single(entry => entry.Name == "bad").Definition);
        Assert.Single(notes);
    }

    [Fact]
    public void ParseSingle_UsesNameFromDirectory()
    {
        var notes = new List<string>();

        var entry = McpConfigParser.ParseSingle(
            """{"command":"x"}""",
            "from-dir",
            McpServerSource.Project,
            "server.json",
            notes);

        Assert.Equal("from-dir", entry.Name);
        Assert.NotNull(entry.Definition);
        Assert.Empty(notes);
    }

    [Fact]
    public void ParseSingle_ReportsInvalidJson()
    {
        var notes = new List<string>();

        var entry = McpConfigParser.ParseSingle(
            "{",
            "broken",
            McpServerSource.Home,
            "server.json",
            notes);

        Assert.Null(entry.Definition);
        Assert.Contains("not valid JSON", Assert.Single(notes));
    }

    [Fact]
    public void ParseFile_NeverRepeatsConfiguredValuesInNotes()
    {
        var notes = new List<string>();

        McpConfigParser.ParseFile(
            """{"mcpServers":{"a":{"url":"https://e.test","headers":{"Authorization":"Bearer sk-secret"},"bogus":1}}}""",
            McpServerSource.Home,
            Origin,
            notes);

        Assert.DoesNotContain("sk-secret", Assert.Single(notes));
    }
}
