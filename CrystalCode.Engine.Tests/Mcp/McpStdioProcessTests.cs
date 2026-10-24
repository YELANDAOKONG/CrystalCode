using System.Diagnostics;

using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Mcp;
using CrystalCode.Engine.Tests.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Mcp;

/// <summary>
/// Runs a real child process that speaks MCP over stdio. It covers what the
/// in-process server cannot: starting the process, the working directory,
/// environment expansion, and the stderr tail on a failed start.
/// </summary>
public sealed class McpStdioProcessTests
{
    private const string Script = """
        import json, os, sys

        def send(message):
            sys.stdout.write(json.dumps(message) + "\n")
            sys.stdout.flush()

        for line in sys.stdin:
            line = line.strip()
            if not line:
                continue
            request = json.loads(line)
            method = request.get("method")
            identifier = request.get("id")
            if method == "initialize":
                send({"jsonrpc": "2.0", "id": identifier, "result": {
                    "protocolVersion": request["params"]["protocolVersion"],
                    "capabilities": {"tools": {}},
                    "serverInfo": {"name": "fake", "version": "1"}}})
            elif method == "tools/list":
                send({"jsonrpc": "2.0", "id": identifier, "result": {"tools": [{
                    "name": "where",
                    "description": "Reports the working directory and a variable.",
                    "inputSchema": {"type": "object", "properties": {}}}]}})
            elif method == "tools/call":
                text = os.getcwd() + "|" + os.environ.get("CRYSTAL_TEST_VALUE", "unset")
                send({"jsonrpc": "2.0", "id": identifier, "result": {
                    "content": [{"type": "text", "text": text}]}})
        """;

    [Fact]
    public async Task ConnectAsync_StartsProcessInWorkspaceWithExpandedEnvironment()
    {
        if (!HasPython())
        {
            return;
        }

        using var root = new TemporaryWorkspace();
        var script = Path.Combine(root.Path, "server.py");
        File.WriteAllText(script, Script);
        var variable = "CRYSTAL_TEST_SOURCE_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(variable, "expanded-value");
        try
        {
            var definition = new McpServerDefinition(
                "fake",
                McpServerSource.Home,
                McpTransportKind.Stdio,
                command: "python3",
                arguments: [script],
                environment: new Dictionary<string, string>
                {
                    ["CRYSTAL_TEST_VALUE"] = "${" + variable + "}"
                });

            await using var catalog = await McpCatalog.ConnectAsync(
                [definition],
                root.Path,
                McpToolApprovalSettings.Default,
                new HashSet<string>(StringComparer.Ordinal));

            Assert.Empty(catalog.Notes);
            var tool = Assert.Single(catalog.WorkTools);
            var output = await tool.InvokeAsync(JsonToolCall("1", "mcp__fake__where", "{}"));
            Assert.Equal(Crystal.Tools.ToolResultStatus.Success, output.Status);
            var parts = output.Text.Split('|');
            // macOS reports /private/var for a /var path, so compare real locations.
            Assert.Equal(RealPath(root.Path), RealPath(parts[0]));
            Assert.Equal("expanded-value", parts[1]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public async Task ConnectAsync_DoesNotWaitForTheDefaultDiscoverProbe_OnOlderServers()
    {
        if (!HasPython())
        {
            return;
        }

        using var root = new TemporaryWorkspace();
        var script = Path.Combine(root.Path, "server.py");
        File.WriteAllText(script, Script);
        var definition = new McpServerDefinition(
            "fake",
            McpServerSource.Home,
            McpTransportKind.Stdio,
            command: "python3",
            arguments: [script]);
        var watch = Stopwatch.StartNew();

        await using var catalog = await McpCatalog.ConnectAsync(
            [definition],
            root.Path,
            McpToolApprovalSettings.Default,
            new HashSet<string>(StringComparer.Ordinal));

        // The SDK waits 5 seconds by default for a server/discover answer that an
        // older server never sends.
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"Connecting took {watch.Elapsed}.");
        Assert.Single(catalog.WorkTools);
    }

    [Fact]
    public async Task ConnectAsync_ReportsMissingEnvironmentVariableByNameOnly()
    {
        using var root = new TemporaryWorkspace();
        var missing = "CRYSTAL_TEST_MISSING_" + Guid.NewGuid().ToString("N");
        var definition = new McpServerDefinition(
            "fake",
            McpServerSource.Home,
            McpTransportKind.Stdio,
            command: "does-not-matter",
            environment: new Dictionary<string, string> { ["TOKEN"] = "prefix-${" + missing + "}" });

        await using var catalog = await McpCatalog.ConnectAsync(
            [definition],
            root.Path,
            McpToolApprovalSettings.Default,
            new HashSet<string>(StringComparer.Ordinal));

        var note = Assert.Single(catalog.Notes);
        Assert.Contains(missing, note, StringComparison.Ordinal);
        Assert.DoesNotContain("prefix-", note, StringComparison.Ordinal);
        Assert.Equal(McpConnectionStatus.Failed, Assert.Single(catalog.Servers).Status);
        Assert.Empty(catalog.WorkTools);
    }

    [Fact]
    public async Task ConnectAsync_KeepsOtherServers_WhenOneCommandDoesNotExist()
    {
        if (!HasPython())
        {
            return;
        }

        using var root = new TemporaryWorkspace();
        var script = Path.Combine(root.Path, "server.py");
        File.WriteAllText(script, Script);
        var good = new McpServerDefinition(
            "good",
            McpServerSource.Home,
            McpTransportKind.Stdio,
            command: "python3",
            arguments: [script]);
        var bad = new McpServerDefinition(
            "bad",
            McpServerSource.Home,
            McpTransportKind.Stdio,
            command: "crystal-no-such-command-" + Guid.NewGuid().ToString("N"));

        await using var catalog = await McpCatalog.ConnectAsync(
            [good, bad],
            root.Path,
            McpToolApprovalSettings.Default,
            new HashSet<string>(StringComparer.Ordinal));

        Assert.Equal(
            ["bad:Failed", "good:Connected"],
            catalog.Servers.Select(server => $"{server.Name}:{server.Status}").Order().ToArray());
        Assert.Single(catalog.Notes);
        Assert.Equal("mcp__good__where", Assert.Single(catalog.WorkTools).Definition.Name);
    }

    private static string RealPath(string path) =>
        Directory.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName
        ?? Path.GetFullPath(path).Replace("/private/var/", "/var/", StringComparison.Ordinal);

    private static bool HasPython()
    {
        if (OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "python3",
                ArgumentList = { "--version" },
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            return process is not null
                && process.WaitForExit(TimeSpan.FromSeconds(5))
                && process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
