using System.Text.Json.Nodes;

using CrystalCode.Commands;

using Xunit;

namespace CrystalCode.Tests.Commands;

/// <summary>
/// Each case passes its own writers and uses <c>--format text</c>. The Spectre
/// table path writes through the static <c>AnsiConsole</c>, so tests that
/// replaced <c>Console.Out</c> could leave a closed writer behind for every
/// later test that renders. Format parsing is covered by ExtensionConsoleTests.
/// </summary>
public sealed class McpConsoleTests : IDisposable
{
    private const string Config = """
        {"mcpServers":{
          "alpha":{"command":"secret-command","args":["secret-arg"],"env":{"K":"secret-env"}},
          "beta":{"url":"https://secret.example/mcp","headers":{"Authorization":"secret-header"}},
          "bad":{"bogus":1}}}
        """;

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crystal-mcp-console",
        Guid.NewGuid().ToString("N"));

    public McpConsoleTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "home"));
        Directory.CreateDirectory(Path.Combine(_root, "workspace"));
        File.WriteAllText(HomeFile, Config);
    }

    private string HomeFile => Path.Combine(_root, "home", "mcp.json");

    [Fact]
    public void List_PrintsEveryServerAndNoSecrets()
    {
        var (exit, output, error) = Run("list", null);

        Assert.Equal(0, exit);
        Assert.Contains("alpha", output, StringComparison.Ordinal);
        Assert.Contains("beta", output, StringComparison.Ordinal);
        Assert.Contains("bad", output, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", output + error, StringComparison.Ordinal);
    }

    [Fact]
    public void Show_PrintsTheTransportAndNoSecrets()
    {
        var (exit, output, error) = Run("show", "beta");

        Assert.Equal(0, exit);
        Assert.Contains("http", output, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", output + error, StringComparison.Ordinal);
    }

    [Fact]
    public void Disable_ChangesOnlyThatServerAndReportsIt()
    {
        var (exit, output, _) = Run("disable", "alpha");

        Assert.Equal(0, exit);
        Assert.Contains("MCP server alpha disabled.", output, StringComparison.Ordinal);
        var written = JsonNode.Parse(File.ReadAllText(HomeFile))!["mcpServers"]!;
        Assert.False(written["alpha"]!["enabled"]!.GetValue<bool>());
        Assert.Equal("secret-command", written["alpha"]!["command"]!.GetValue<string>());
        Assert.Null(written["beta"]!["enabled"]);
    }

    [Fact]
    public void Disable_Twice_SaysItIsAlreadyDisabled()
    {
        Run("disable", "alpha");

        var (exit, output, _) = Run("disable", "alpha");

        Assert.Equal(0, exit);
        Assert.Contains("is already disabled.", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Enable_AfterDisable_Restores()
    {
        Run("disable", "alpha");

        var (exit, output, _) = Run("enable", "alpha");

        Assert.Equal(0, exit);
        Assert.Contains("MCP server alpha enabled.", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Disable_AnInvalidServer_ExitsOneAndLeavesTheFile()
    {
        var before = File.ReadAllText(HomeFile);

        var (exit, _, error) = Run("disable", "bad");

        Assert.Equal(1, exit);
        Assert.Contains("is invalid", error, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(HomeFile));
    }

    [Theory]
    [InlineData("show", "missing", "was not found")]
    [InlineData("enable", null, "Server is required.")]
    public void Failures_ExitOneWithAnExplanationOnStderr(string verb, string? server, string expected)
    {
        var (exit, output, error) = Run(verb, server);

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, output.Trim());
        Assert.Contains(expected, error, StringComparison.Ordinal);
    }

    [Fact]
    public void BadSource_ExitsOneWithAnExplanationOnStderr()
    {
        var (exit, _, error) = Run("list", null, source: "both");

        Assert.Equal(1, exit);
        Assert.Contains("Source must be home or project.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceOption_SelectsTheHomeServerWhenProjectReplacesIt()
    {
        var project = Path.Combine(_root, "workspace", ".crystal");
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, "mcp.json"), """{"mcpServers":{"alpha":{"command":"project-cmd"}}}""");

        Run("disable", "alpha", source: "home");

        Assert.False(JsonNode.Parse(File.ReadAllText(HomeFile))!["mcpServers"]!["alpha"]!["enabled"]!.GetValue<bool>());
        Assert.Null(JsonNode.Parse(File.ReadAllText(Path.Combine(project, "mcp.json")))!["mcpServers"]!["alpha"]!["enabled"]);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private (int Exit, string Output, string Error) Run(
        string verb,
        string? server,
        string format = "text",
        string? source = null)
    {
        var settings = new NamedMcpSettings
        {
            Home = Path.Combine(_root, "home"),
            Workspace = Path.Combine(_root, "workspace"),
            Format = format,
            Source = source,
            Server = server ?? string.Empty
        };
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = McpConsole.Run(settings, server, verb, output, error);
        return (exit, output.ToString(), error.ToString());
    }
}
