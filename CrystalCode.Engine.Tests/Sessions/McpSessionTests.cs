using System.Diagnostics;

using Crystal;
using Crystal.Chat;
using Crystal.Tools;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Tests.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

/// <summary>
/// Drives a real <see cref="Sessions.CodingSession"/> against a real child
/// process that speaks MCP, configured through <c>mcp.json</c>.
/// </summary>
public sealed class McpSessionTests
{
    private const string Server = """
        import json, os, sys, threading, time

        lock = threading.Lock()

        def send(message):
            with lock:
                sys.stdout.write(json.dumps(message) + "\n")
                sys.stdout.flush()

        def announce_when_flagged():
            flag = os.path.join(os.path.dirname(os.path.abspath(__file__)), "more.flag")
            while not os.path.exists(flag):
                time.sleep(0.05)
            send({"jsonrpc": "2.0", "method": "notifications/tools/list_changed"})

        threading.Thread(target=announce_when_flagged, daemon=True).start()

        with open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "server.pid"), "w") as handle:
            handle.write(str(os.getpid()))

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
                    "capabilities": {"tools": {"listChanged": True}, "resources": {}, "prompts": {}},
                    "serverInfo": {"name": "fake", "version": "1"}}})
            elif method == "resources/list":
                send({"jsonrpc": "2.0", "id": identifier, "result": {"resources": [{
                    "uri": "file:///notes.txt", "name": "notes", "mimeType": "text/plain"}]}})
            elif method == "resources/read":
                send({"jsonrpc": "2.0", "id": identifier, "result": {"contents": [{
                    "uri": request["params"]["uri"], "mimeType": "text/plain", "text": "resource body"}]}})
            elif method == "prompts/list":
                send({"jsonrpc": "2.0", "id": identifier, "result": {"prompts": [{
                    "name": "greet", "description": "Greets someone.",
                    "arguments": [{"name": "who", "required": True}]}]}})
            elif method == "prompts/get":
                who = request["params"].get("arguments", {}).get("who", "nobody")
                send({"jsonrpc": "2.0", "id": identifier, "result": {"messages": [{
                    "role": "user", "content": {"type": "text", "text": "Please greet " + who}}]}})
            elif method == "tools/list":
                names = ["where"]
                if os.path.exists(os.path.join(os.path.dirname(os.path.abspath(__file__)), "more.flag")):
                    names.append("extra")
                tools = [{
                    "name": name,
                    "description": "Reports the working directory.",
                    "inputSchema": {"type": "object", "properties": {}}} for name in names]
                tools.append({
                    "name": "peek",
                    "description": "Declares itself read-only.",
                    "inputSchema": {"type": "object", "properties": {}},
                    "annotations": {"readOnlyHint": True}})
                send({"jsonrpc": "2.0", "id": identifier, "result": {"tools": tools}})

            elif method == "tools/call":
                send({"jsonrpc": "2.0", "id": identifier, "result": {
                    "content": [{"type": "text", "text": "cwd=" + os.path.basename(os.getcwd())}]}})
        """;

    [Fact]
    public async Task Start_ConnectsConfiguredServerAndOffersItsToolInWorkOnly()
    {
        if (!HasPython())
        {
            return;
        }

        using var headless = Open();

        await headless.Session.StartAsync(CancellationToken.None);
        await headless.Session.SubmitAsync("/tools", CancellationToken.None);

        var listed = headless.Observer.Events.OfType<ToolsListed>().Last();
        Assert.Contains(listed.Report.Work, tool => tool.Name == "mcp__fake__where");
        Assert.DoesNotContain(listed.Report.Plan, tool => tool.Name == "mcp__fake__where");
        Assert.DoesNotContain(Errors(headless), text => text.Contains("MCP", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Turn_CallsTheServerToolAfterApproval()
    {
        if (!HasPython())
        {
            return;
        }

        var client = new ScriptedStreamingClient(
            ToolRound("c1", "mcp__fake__where", "{}"),
            TextRound("done"));
        using var headless = Open(client, ApprovalMode.Full);
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.RunTurnAsync("where am I");

        var received = headless.Observer.Events.OfType<ToolResultsReceived>().Single();
        var result = Assert.Single(received.Results);
        Assert.Equal(ToolResultStatus.Success, result.Status);
        Assert.StartsWith("cwd=", result.Text, StringComparison.Ordinal);
        Assert.Equal(Path.GetFileName(headless.WorkspacePath), result.Text["cwd=".Length..]);
    }

    [Fact]
    public async Task Turn_AsksBeforeRunningTheServerTool_InDefaultMode()
    {
        if (!HasPython())
        {
            return;
        }

        var client = new ScriptedStreamingClient(
            ToolRound("c1", "mcp__fake__where", "{}"),
            TextRound("done"));
        using var headless = Open(client);
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.RunTurnAsync("where am I");

        Assert.Equal(1, headless.Approvals.Count);
        Assert.Equal(Risk.Write, headless.Approvals.LastClassification!.Risk);
        var result = Assert.Single(headless.Observer.Events.OfType<ToolResultsReceived>().Single().Results);
        Assert.NotEqual(ToolResultStatus.Success, result.Status);
    }

    [Fact]
    public async Task McpOff_DisconnectsAndRemovesTheTool()
    {
        if (!HasPython())
        {
            return;
        }

        using var headless = Open();
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.Session.SubmitAsync("/mcp off", CancellationToken.None);
        await headless.Session.SubmitAsync("/tools", CancellationToken.None);

        var listed = headless.Observer.Events.OfType<ToolsListed>().Last();
        Assert.DoesNotContain(listed.Report.Work, tool => tool.Name == "mcp__fake__where");
        Assert.Contains(Notes(headless), text => text == "MCP  Off");
        Assert.Contains("\"mcp\": false", File.ReadAllText(headless.Home.ConfigPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task McpStatus_ListsTheServerWithoutItsConfiguration()
    {
        if (!HasPython())
        {
            return;
        }

        using var headless = Open();
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.Session.SubmitAsync("/mcp", CancellationToken.None);

        var status = Notes(headless).Last();
        Assert.Contains("fake  Home  stdio  Connected  2 tools", status, StringComparison.Ordinal);
        Assert.DoesNotContain("python3", status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task McpReload_PicksUpAServerAddedAfterStart()
    {
        if (!HasPython())
        {
            return;
        }

        using var headless = Open();
        await headless.Session.StartAsync(CancellationToken.None);
        WriteConfig(headless.Home.Root, headless.WorkspacePath, ["fake", "second"]);

        await headless.Session.SubmitAsync("/mcp reload", CancellationToken.None);
        await headless.Session.SubmitAsync("/tools", CancellationToken.None);

        var listed = headless.Observer.Events.OfType<ToolsListed>().Last();
        Assert.Contains(listed.Report.Work, tool => tool.Name == "mcp__second__where");
        Assert.Contains(Notes(headless), text => text == "MCP reloaded");
    }

    [Fact]
    public async Task ChangeDirectory_ReconnectsServersOfTheNewWorkspace()
    {
        if (!HasPython())
        {
            return;
        }

        using var other = new TemporaryWorkspace();
        var project = Path.Combine(other.Path, ".crystal");
        Directory.CreateDirectory(project);
        File.WriteAllText(
            Path.Combine(project, "mcp.json"),
            ConfigJson(Path.Combine(other.Path, "server.py"), ["project_only"]));
        File.WriteAllText(Path.Combine(other.Path, "server.py"), Server);
        using var headless = Open();
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.Session.SubmitAsync("/cd " + other.Path, CancellationToken.None);
        await headless.Session.SubmitAsync("/tools", CancellationToken.None);

        var names = headless.Observer.Events.OfType<ToolsListed>().Last().Report.Work.Select(tool => tool.Name).ToArray();
        Assert.Contains("mcp__project_only__where", names);
        Assert.Contains("mcp__fake__where", names);
    }

    [Fact]
    public async Task Start_OffersTheResourceToolsAndTheServerPromptCommand()
    {
        if (!HasPython())
        {
            return;
        }

        using var headless = Open();

        await headless.Session.StartAsync(CancellationToken.None);
        await headless.Session.SubmitAsync("/tools", CancellationToken.None);

        var work = headless.Observer.Events.OfType<ToolsListed>().Last().Report.Work.Select(tool => tool.Name).ToArray();
        Assert.Contains("list_mcp_resources", work);
        Assert.Contains("read_mcp_resource", work);
        var menu = headless.Observer.Events.OfType<SlashCommandsChanged>().Last().Commands;
        var command = Assert.Single(menu, item => item.Name == "mcp__fake__greet");
        Assert.Equal("Greets someone.", command.Help);
    }

    [Fact]
    public async Task Turn_ReadsAResourceThroughTheHostTool()
    {
        if (!HasPython())
        {
            return;
        }

        var client = new ScriptedStreamingClient(
            ToolRound("c1", "read_mcp_resource", """{"server":"fake","uri":"file:///notes.txt"}"""),
            TextRound("done"));
        using var headless = Open(client, ApprovalMode.Full);
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.RunTurnAsync("read the notes");

        var result = Assert.Single(headless.Observer.Events.OfType<ToolResultsReceived>().Single().Results);
        Assert.Equal(ToolResultStatus.Success, result.Status);
        Assert.Equal("resource body", result.Text);
    }

    [Fact]
    public async Task PromptCommand_SubmitsTheServerTextAsTheUserMessage()
    {
        if (!HasPython())
        {
            return;
        }

        var client = new ScriptedStreamingClient(TextRound("hello"));
        using var headless = Open(client);
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.RunTurnAsync("/mcp__fake__greet who=Ada");

        Assert.Equal("Please greet Ada", headless.Observer.Events.OfType<UserMessageSent>().Single().Text);
        Assert.Equal(1, client.RequestCount);
    }

    [Fact]
    public async Task PromptCommand_ReportsAMissingRequiredArgumentWithoutStartingATurn()
    {
        if (!HasPython())
        {
            return;
        }

        var client = new ScriptedStreamingClient(TextRound("unused"));
        using var headless = Open(client);
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.RunTurnAsync("/mcp__fake__greet");

        Assert.Contains(Errors(headless), text => text.Contains("needs 'who'", StringComparison.Ordinal));
        Assert.Empty(headless.Observer.Events.OfType<UserMessageSent>());
        Assert.Equal(0, client.RequestCount);
    }

    [Fact]
    public async Task PromptCommand_TypedDuringATurnIsRefusedAndStartsNoSecondTurn()
    {
        if (!HasPython())
        {
            return;
        }

        var client = new BlockingStreamingClient();
        using var headless = Open(client);
        await headless.Session.StartAsync(CancellationToken.None);
        await headless.Session.SubmitAsync("long task", CancellationToken.None);
        await client.Started.WaitAsync(TimeSpan.FromSeconds(10));

        await headless.Session.SubmitAsync("/mcp__fake__greet who=Ada", CancellationToken.None);

        Assert.Contains(
            Errors(headless),
            text => text == "Finish the current turn before using /mcp__fake__greet.");
        Assert.Equal(["long task"], headless.Observer.Events.OfType<UserMessageSent>().Select(sent => sent.Text).ToArray());
        headless.Session.TryInterrupt();
        await headless.Session.FinishTurnAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ToolListChanged_RefreshesTheToolsAfterTheTurn()
    {
        if (!HasPython())
        {
            return;
        }

        var client = new ScriptedStreamingClient(TextRound("done"));
        using var headless = Open(client);
        await headless.Session.StartAsync(CancellationToken.None);
        File.WriteAllText(Path.Combine(headless.WorkspacePath, "more.flag"), "x");
        await Task.Delay(500);

        await headless.RunTurnAsync("anything");
        await headless.Session.SubmitAsync("/tools", CancellationToken.None);

        var work = headless.Observer.Events.OfType<ToolsListed>().Last().Report.Work.Select(tool => tool.Name).ToArray();
        Assert.Contains("mcp__fake__extra", work);
        Assert.Contains(Notes(headless), text => text == "MCP tools changed  fake");
    }

    [Fact]
    public async Task McpList_ShowsDisabledServersWithoutTheirConfiguration()
    {
        if (!HasPython())
        {
            return;
        }

        using var headless = Open();
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.Session.SubmitAsync("/mcp list", CancellationToken.None);

        var table = string.Join('\n', Notes(headless).Skip(Notes(headless).Count() - 2));
        Assert.Contains("fake", table, StringComparison.Ordinal);
        Assert.Contains("Home", table, StringComparison.Ordinal);
        Assert.DoesNotContain("python3", table, StringComparison.Ordinal);
    }

    [Fact]
    public async Task McpDisable_StopsTheServerKeepsItListedAndRewritesTheFile()
    {
        if (!HasPython())
        {
            return;
        }

        using var headless = Open();
        await headless.Session.StartAsync(CancellationToken.None);
        var pid = ReadPid(headless);

        await headless.Session.SubmitAsync("/mcp disable fake", CancellationToken.None);
        await headless.Session.SubmitAsync("/tools", CancellationToken.None);
        await headless.Session.SubmitAsync("/mcp list", CancellationToken.None);

        Assert.True(await WaitUntilGoneAsync(pid), "The disabled server was still running.");
        Assert.DoesNotContain(
            headless.Observer.Events.OfType<ToolsListed>().Last().Report.Work,
            tool => tool.Name == "mcp__fake__where");
        Assert.Contains(Notes(headless), text => text == "MCP server fake  Disabled");
        Assert.Contains("\"enabled\": false", File.ReadAllText(Path.Combine(headless.Home.Root, "mcp.json")), StringComparison.Ordinal);
        Assert.Contains(Notes(headless), text => text.StartsWith("fake", StringComparison.Ordinal) && text.Contains("No", StringComparison.Ordinal));
    }

    [Fact]
    public async Task McpEnable_ConnectsTheServerAgain()
    {
        if (!HasPython())
        {
            return;
        }

        using var headless = Open();
        await headless.Session.StartAsync(CancellationToken.None);
        await headless.Session.SubmitAsync("/mcp disable fake", CancellationToken.None);

        await headless.Session.SubmitAsync("/mcp enable fake", CancellationToken.None);
        await headless.Session.SubmitAsync("/tools", CancellationToken.None);

        Assert.Contains(
            headless.Observer.Events.OfType<ToolsListed>().Last().Report.Work,
            tool => tool.Name == "mcp__fake__where");
        Assert.Contains(Notes(headless), text => text == "MCP server fake  Enabled");
    }

    [Fact]
    public async Task McpDisable_RefusesDuringATurn()
    {
        if (!HasPython())
        {
            return;
        }

        var client = new BlockingStreamingClient();
        using var headless = Open(client);
        await headless.Session.StartAsync(CancellationToken.None);
        await headless.Session.SubmitAsync("long task", CancellationToken.None);
        await client.Started.WaitAsync(TimeSpan.FromSeconds(10));

        await headless.Session.SubmitAsync("/mcp disable fake", CancellationToken.None);

        Assert.DoesNotContain("enabled", File.ReadAllText(Path.Combine(headless.Home.Root, "mcp.json")), StringComparison.Ordinal);
        headless.Session.TryInterrupt();
        await headless.Session.FinishTurnAsync(CancellationToken.None);
    }

    [Fact]
    public async Task McpShow_ReportsTheLiveState()
    {
        if (!HasPython())
        {
            return;
        }

        using var headless = Open();
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.Session.SubmitAsync("/mcp show fake", CancellationToken.None);

        Assert.Contains(Notes(headless), text => text == "State  Connected, 2 tools");
        Assert.DoesNotContain(Notes(headless), text => text.Contains("python3", StringComparison.Ordinal));
    }

    [Fact]
    public async Task McpApproval_SetsAndShowsThePolicyForOneSource()
    {
        if (!HasPython())
        {
            return;
        }

        using var headless = Open();
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.Session.SubmitAsync("/mcp home author", CancellationToken.None);
        await headless.Session.SubmitAsync("/mcp approval", CancellationToken.None);

        Assert.Contains(Notes(headless), text => text == "MCP approval  Home  Author");
        Assert.Contains(Notes(headless), text => text == "MCP approval  Home Author, Project Host");
        Assert.Contains("\"home\": \"author\"", File.ReadAllText(headless.Home.ConfigPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task McpApproval_Host_ReviewsEvenAToolThatDeclaresItselfReadOnly()
    {
        if (!HasPython())
        {
            return;
        }

        var client = new ScriptedStreamingClient(ToolRound("c1", "mcp__fake__peek", "{}"), TextRound("done"));
        using var headless = Open(client);
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.RunTurnAsync("peek");

        Assert.Equal(1, headless.Approvals.Count);
        Assert.Equal(Risk.Write, headless.Approvals.LastClassification!.Risk);
    }

    [Fact]
    public async Task McpApproval_Author_LowersOnlyTheToolThatDeclaredItselfReadOnly()
    {
        if (!HasPython())
        {
            return;
        }

        var client = new ScriptedStreamingClient(
            ToolRound("c1", "mcp__fake__peek", "{}"),
            ToolRound("c2", "mcp__fake__where", "{}"),
            TextRound("done"));
        using var headless = Open(client);
        await headless.Session.StartAsync(CancellationToken.None);
        await headless.Session.SubmitAsync("/mcp home author", CancellationToken.None);

        await headless.RunTurnAsync("peek then where");

        // peek is read-only and passes without asking; where declared nothing and is still reviewed.
        Assert.Equal(1, headless.Approvals.Count);
        Assert.Equal(Risk.Write, headless.Approvals.LastClassification!.Risk);
        var results = headless.Observer.Events.OfType<ToolResultsReceived>().SelectMany(received => received.Results).ToArray();
        Assert.Equal(ToolResultStatus.Success, results[0].Status);
    }

    [Theory]
    [InlineData("/mcp bogus")]
    [InlineData("/mcp enable")]
    [InlineData("/mcp home maybe")]
    [InlineData("/mcp enable fake sideways")]
    public async Task Mcp_RejectsMalformedCommandsWithTheUsage(string input)
    {
        if (!HasPython())
        {
            return;
        }

        using var headless = Open();
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.Session.SubmitAsync(input, CancellationToken.None);

        Assert.Contains(Errors(headless), text => text.StartsWith("MCP command must be", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Status_ReportsMcpSwitchServersAndTools()
    {
        if (!HasPython())
        {
            return;
        }

        using var headless = Open();
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.Session.SubmitAsync("/status full", CancellationToken.None);

        var status = headless.Observer.Events.OfType<StatusReported>().Last().Status;
        Assert.True(status.McpEnabled);
        Assert.Equal(1, status.McpServers);
        Assert.Equal(2, status.McpTools);
    }

    [Fact]
    public async Task Close_StopsTheServerProcess()
    {
        if (!HasPython())
        {
            return;
        }

        using var headless = Open();
        await headless.Session.StartAsync(CancellationToken.None);
        var pid = ReadPid(headless);
        Assert.True(IsAlive(pid));

        await headless.Session.CloseAsync(CancellationToken.None);

        Assert.True(await WaitUntilGoneAsync(pid), "The MCP server process was still running after close.");
    }

    [Fact]
    public async Task Clear_KeepsTheSameServerProcess()
    {
        if (!HasPython())
        {
            return;
        }

        using var headless = Open();
        await headless.Session.StartAsync(CancellationToken.None);
        var pid = ReadPid(headless);

        await headless.Session.SubmitAsync("/clear", CancellationToken.None);
        await headless.Session.SubmitAsync("/mcp", CancellationToken.None);

        Assert.True(IsAlive(pid));
        Assert.Equal(pid, ReadPid(headless));
        Assert.Contains("fake  Home  stdio  Connected  2 tools", Notes(headless).Last(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task McpReload_StopsTheOldProcessBeforeStartingTheNewOne()
    {
        if (!HasPython())
        {
            return;
        }

        using var headless = Open();
        await headless.Session.StartAsync(CancellationToken.None);
        var first = ReadPid(headless);

        await headless.Session.SubmitAsync("/mcp reload", CancellationToken.None);

        Assert.True(await WaitUntilGoneAsync(first), "The old MCP server process was still running after reload.");
        Assert.True(IsAlive(ReadPid(headless)));
    }

    [Fact]
    public async Task Start_ReportsAServerThatCannotStart_AndKeepsTheSessionUsable()
    {
        using var headless = Open(configure: (home, _) =>
        {
            Directory.CreateDirectory(home.Root);
            File.WriteAllText(
                Path.Combine(home.Root, "mcp.json"),
                """{"mcpServers":{"broken":{"command":"crystal-no-such-command-for-tests"}}}""");
        });

        await headless.Session.StartAsync(CancellationToken.None);
        await headless.Session.SubmitAsync("/mcp", CancellationToken.None);

        Assert.Contains(Notes(headless), text => text.Contains("MCP server 'broken'", StringComparison.Ordinal));
        var status = Notes(headless).Last();
        Assert.Contains("broken  Home  stdio  Failed  0 tools", status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaunchOverride_Off_ConnectsNothing()
    {
        if (!HasPython())
        {
            return;
        }

        using var headless = Open(mcp: false);

        await headless.Session.StartAsync(CancellationToken.None);
        await headless.Session.SubmitAsync("/tools", CancellationToken.None);

        var listed = headless.Observer.Events.OfType<ToolsListed>().Last();
        Assert.DoesNotContain(listed.Report.Work, tool => tool.Name.StartsWith("mcp__", StringComparison.Ordinal));
    }

    private static HeadlessSession Open(
        IStreamingChatClient? client = null,
        ApprovalMode? approval = null,
        bool mcp = true,
        Action<CrystalHome, string>? configure = null)
    {
        var settings = HeadlessSession.ScriptedSettings().WithMcp(mcp);
        if (approval is not null)
        {
            settings = settings.WithApproval(approval);
        }

        // The scripted trust prompt declines, so keep the gate off and test MCP alone.
        settings = settings.WithWorkspaceTrust(false);

        return new HeadlessSession(
            client ?? new ScriptedStreamingClient(),
            settings: settings,
            prepare: configure ?? ((home, workspace) => WriteConfig(home.Root, workspace, ["fake"])));
    }

    private static void WriteConfig(string homeRoot, string workspace, string[] names)
    {
        var script = Path.Combine(workspace, "server.py");
        File.WriteAllText(script, Server);
        Directory.CreateDirectory(homeRoot);
        File.WriteAllText(Path.Combine(homeRoot, "mcp.json"), ConfigJson(script, names));
    }

    private static string ConfigJson(string script, string[] names)
    {
        var entries = names.Select(name =>
            $"\"{name}\":{{\"command\":\"python3\",\"args\":[\"{script.Replace("\\", "\\\\")}\"]}}");
        return "{\"mcpServers\":{" + string.Join(",", entries) + "}}";
    }

    private static int ReadPid(HeadlessSession headless) =>
        int.Parse(File.ReadAllText(Path.Combine(headless.WorkspacePath, "server.pid")));

    private static bool IsAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static async Task<bool> WaitUntilGoneAsync(int pid)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (!IsAlive(pid))
            {
                return true;
            }

            await Task.Delay(100);
        }

        return false;
    }

    private static IEnumerable<string> Notes(HeadlessSession headless) =>
        headless.Observer.Events.OfType<NoteWritten>().Select(note => note.Text);

    private static IEnumerable<string> Errors(HeadlessSession headless) =>
        headless.Observer.Events.OfType<ErrorWritten>().Select(error => error.Text);

    private static ChatStreamEvent[] TextRound(string text) =>
    [
        new ChatTextDelta(0, 0, ChatRole.Assistant, text),
        new ChatCandidateCompleted(0, FinishReason.Stop)
    ];

    private static ChatStreamEvent[] ToolRound(string callId, string name, string arguments) =>
    [
        JsonChatToolCallDelta(0, 0, callId, name, arguments),
        new ChatCandidateCompleted(0, FinishReason.ToolCalls)
    ];

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
