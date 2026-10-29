using System.Text.Json;
using System.Text.Json.Nodes;

using CrystalCode.Bridge.Tests.Support;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Tools;

using Xunit;

namespace CrystalCode.Bridge.Tests;

/// <summary>Load, resume, list, config options, modes, commands, and reports.</summary>
public sealed class BridgeSessionTests
{
    [Fact]
    public async Task New_ReturnsConfigOptionsAndModes()
    {
        await using var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false));
        await fixture.RequestAsync("initialize", new { protocolVersion = 1 });

        var created = await fixture.RequestAsync("session/new", new { cwd = fixture.Workspace, mcpServers = Array.Empty<object>() });

        var result = created.GetProperty("result");
        var options = result.GetProperty("configOptions").EnumerateArray().ToArray();
        Assert.Equal("mode", options[0].GetProperty("id").GetString());
        Assert.Equal("work", options[0].GetProperty("currentValue").GetString());
        var model = options.Single(static option => option.GetProperty("id").GetString() == "model");
        Assert.Equal("model", model.GetProperty("category").GetString());
        Assert.Equal("ollama/qwen3:8b", model.GetProperty("currentValue").GetString());
        Assert.Contains(model.GetProperty("options").EnumerateArray(), static group => group.GetProperty("group").GetString() == "ollama");
        var agents = options.Single(static option => option.GetProperty("id").GetString() == "agents");
        Assert.Equal("select", agents.GetProperty("type").GetString());
        var modes = result.GetProperty("modes");
        Assert.Equal("work", modes.GetProperty("currentModeId").GetString());
        Assert.Contains(modes.GetProperty("availableModes").EnumerateArray(), static mode => mode.GetProperty("id").GetString() == "plan");
    }

    [Fact]
    public async Task BooleanSwitches_NeedTheClientCapability()
    {
        await using var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false));
        await fixture.RequestAsync("initialize", new
        {
            protocolVersion = 1,
            clientCapabilities = new { session = new { configOptions = new { boolean = new { } } } }
        });

        var created = await fixture.RequestAsync("session/new", new { cwd = fixture.Workspace, mcpServers = Array.Empty<object>() });

        var agents = created.GetProperty("result").GetProperty("configOptions").EnumerateArray()
            .Single(static option => option.GetProperty("id").GetString() == "agents");
        Assert.Equal("boolean", agents.GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.False, agents.GetProperty("currentValue").ValueKind);
    }

    [Fact]
    public async Task SetMode_RunsTheModeCommandAndReportsTheChange()
    {
        await using var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false));
        var id = await fixture.OpenAsync();

        var set = await fixture.RequestAsync("session/set_mode", new { sessionId = id, modeId = "plan" });
        var bad = await fixture.RequestAsync("session/set_mode", new { sessionId = id, modeId = "nope" });

        Assert.True(set.TryGetProperty("result", out _));
        Assert.Equal(-32602, bad.GetProperty("error").GetProperty("code").GetInt32());
        var update = await fixture.WaitForUpdateAsync("current_mode_update", static update => update.GetProperty("currentModeId").GetString() == "plan");
        Assert.Equal("plan", update.GetProperty("params").GetProperty("update").GetProperty("currentModeId").GetString());
    }

    [Fact]
    public async Task SetConfigOption_ReturnsTheCompleteListAndRejectsUnofferedValues()
    {
        await using var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false));
        var id = await fixture.OpenAsync();

        var set = await fixture.RequestAsync("session/set_config_option", new { sessionId = id, configId = "approval", value = "edit" });
        var unknown = await fixture.RequestAsync("session/set_config_option", new { sessionId = id, configId = "nope", value = "x" });
        var unoffered = await fixture.RequestAsync("session/set_config_option", new { sessionId = id, configId = "approval", value = "sometimes" });

        var options = set.GetProperty("result").GetProperty("configOptions").EnumerateArray().ToArray();
        Assert.Equal("edit", options.Single(static option => option.GetProperty("id").GetString() == "approval").GetProperty("currentValue").GetString());
        Assert.Contains(options, static option => option.GetProperty("id").GetString() == "mode");
        Assert.Equal(-32602, unknown.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(-32602, unoffered.GetProperty("error").GetProperty("code").GetInt32());
        var config = JsonNode.Parse(File.ReadAllText(Path.Combine(fixture.Home, "config.json")))!;
        Assert.Contains("edit", config.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetConfigOption_WhileBusy_AppliesNextTurn()
    {
        var model = new ScriptedModel(true);
        await using var fixture = await BridgeFixture.StartAsync(model);
        var id = await fixture.OpenAsync();
        var held = model.Held.Task;
        var (_, prompt) = await fixture.StartRequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "long" } } });
        await held.WaitAsync(BridgeFixture.Wait);

        var set = await fixture.RequestAsync("session/set_config_option", new { sessionId = id, configId = "approval", value = "edit" });
        var update = await fixture.WaitForUpdateAsync("config_option_update", static update =>
            update.TryGetProperty("_meta", out var meta)
            && meta.GetProperty("crystal").TryGetProperty("pending", out var pending)
            && pending.TryGetProperty("approval", out _));
        await fixture.NotifyAsync("session/cancel", new { sessionId = id });
        await prompt.WaitAsync(BridgeFixture.Wait);

        Assert.False(set.TryGetProperty("error", out _));
        var approval = set.GetProperty("result").GetProperty("configOptions").EnumerateArray()
            .Single(static option => option.GetProperty("id").GetString() == "approval");
        Assert.Equal("default", approval.GetProperty("currentValue").GetString());
        Assert.Equal("edit", update.GetProperty("params").GetProperty("update").GetProperty("_meta").GetProperty("crystal").GetProperty("pending").GetProperty("approval").GetString());
        await fixture.WaitForUpdateAsync("config_option_update", static update =>
            update.TryGetProperty("configOptions", out var options)
            && options.EnumerateArray().Any(static option =>
                option.GetProperty("id").GetString() == "approval" && option.GetProperty("currentValue").GetString() == "edit"));
    }

    [Fact]
    public async Task SetConfigOption_WhileBusy_KeepsTheRefusalForAnInvalidValue()
    {
        var model = new ScriptedModel(true);
        await using var fixture = await BridgeFixture.StartAsync(model);
        var id = await fixture.OpenAsync();
        var held = model.Held.Task;
        var (_, prompt) = await fixture.StartRequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "long" } } });
        await held.WaitAsync(BridgeFixture.Wait);

        var refused = await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "/cd .." } } });
        await fixture.NotifyAsync("session/cancel", new { sessionId = id });
        await prompt.WaitAsync(BridgeFixture.Wait);

        var error = refused.GetProperty("error");
        Assert.Equal(-32603, error.GetProperty("code").GetInt32());
        Assert.Equal("command_refused", error.GetProperty("data").GetProperty("crystal").GetProperty("kind").GetString());
    }

    [Fact]
    public async Task AvailableCommands_ListTheSlashVerbsWithoutTheSlash()
    {
        await using var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false));
        await fixture.OpenAsync();

        var update = await fixture.WaitForUpdateAsync("available_commands_update");

        var commands = update.GetProperty("params").GetProperty("update").GetProperty("availableCommands").EnumerateArray().ToArray();
        var thinking = commands.Single(static command => command.GetProperty("name").GetString() == "approval");
        Assert.False(thinking.GetProperty("name").GetString()!.StartsWith('/'));
        Assert.Contains("default", thinking.GetProperty("input").GetProperty("hint").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/help", "help", "/model")]
    [InlineData("/status", "status", "Model")]
    [InlineData("/tools", "tools", "read")]
    [InlineData("/todos", "todos", "No todos")]
    [InlineData("/stats", "stats", "Sessions")]
    public async Task Reports_AreAgentTextFromTheEngineRenderers(string command, string kind, string expected)
    {
        await using var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false));
        var id = await fixture.OpenAsync();

        await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = command } } });

        var report = fixture.Updates().Single(update => update.TryGetProperty("_meta", out var meta)
            && meta.GetProperty("crystal").TryGetProperty("report", out var name)
            && name.GetString() == kind);
        Assert.Equal("agent_message_chunk", report.GetProperty("sessionUpdate").GetString());
        Assert.Contains(expected, report.GetProperty("content").GetProperty("text").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Load_ReplaysTheConversationBeforeItAnswers()
    {
        var model = new ScriptedModel(false,
            ScriptedModel.ToolRound("call-1", "read", """{"path":"a.txt"}"""),
            ScriptedModel.TextRound("the file says hi"));
        await using var fixture = await BridgeFixture.StartAsync(model);
        File.WriteAllText(Path.Combine(fixture.Workspace, "a.txt"), "hi\n");
        var id = await fixture.OpenAsync();
        await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "read a.txt" } } });
        await fixture.RequestAsync("session/close", new { sessionId = id });
        var before = fixture.Lines.Count;

        var loaded = await fixture.RequestAsync("session/load", new { sessionId = id, cwd = fixture.Workspace, mcpServers = Array.Empty<object>() });

        Assert.True(loaded.GetProperty("result").TryGetProperty("configOptions", out _));
        Assert.False(loaded.GetProperty("result").TryGetProperty("sessionId", out _));
        string[] lines;
        lock (fixture.Lines)
        {
            lines = [.. fixture.Lines.Skip(before)];
        }

        var replay = lines
            .Select(static line => JsonDocument.Parse(line).RootElement)
            .TakeWhile(static message => message.TryGetProperty("method", out _))
            .Where(static message => message.GetProperty("params").TryGetProperty("update", out var update)
                && update.TryGetProperty("_meta", out var meta)
                && meta.GetProperty("crystal").GetProperty("event").GetString() == "HistoryReplayed")
            .Select(static message => message.GetProperty("params").GetProperty("update"))
            .ToArray();
        Assert.Equal("user_message_chunk", replay[0].GetProperty("sessionUpdate").GetString());
        Assert.Equal("read a.txt", replay[0].GetProperty("content").GetProperty("text").GetString());
        Assert.Contains(replay, static update => update.GetProperty("sessionUpdate").GetString() == "tool_call");
        Assert.Contains(replay, static update => update.GetProperty("sessionUpdate").GetString() == "tool_call_update"
            && update.GetProperty("status").GetString() == "completed");
        Assert.Equal("the file says hi", replay[^1].GetProperty("content").GetProperty("text").GetString());
        Assert.All(lines.Take(Array.FindIndex(lines, static line => !line.Contains("\"method\"", StringComparison.Ordinal))), static line =>
            Assert.Contains("\"sessionId\":", line, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Resume_RestoresWithoutReplay()
    {
        var model = new ScriptedModel(false, ScriptedModel.TextRound("first"), ScriptedModel.TextRound("second"));
        await using var fixture = await BridgeFixture.StartAsync(model);
        var id = await fixture.OpenAsync();
        await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "remember blue" } } });
        await fixture.RequestAsync("session/close", new { sessionId = id });

        var resumed = await fixture.RequestAsync("session/resume", new { sessionId = id, cwd = fixture.Workspace });
        var after = await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "what color" } } });

        Assert.True(resumed.TryGetProperty("result", out _));
        Assert.DoesNotContain(fixture.Updates(), static update => update.TryGetProperty("_meta", out var meta)
            && meta.GetProperty("crystal").GetProperty("event").GetString() == "HistoryReplayed");
        Assert.Equal("end_turn", after.GetProperty("result").GetProperty("stopReason").GetString());
        Assert.Contains(model.Requests[^1].Items.OfType<Crystal.Chat.ChatMessage>(), static message => message.Text == "remember blue");
    }

    [Fact]
    public async Task Load_IntoAnotherCwd_MovesTheSession()
    {
        await using var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false));
        var other = Directory.CreateDirectory(Path.Combine(fixture.Root, "moved")).FullName;
        new WorkspaceTrustStore(new CrystalHome(fixture.Home)).Remember(other);
        var id = await fixture.OpenAsync();
        await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "move me" } } });
        await fixture.RequestAsync("session/close", new { sessionId = id });

        var loaded = await fixture.RequestAsync("session/load", new { sessionId = id, cwd = other, mcpServers = Array.Empty<object>() });
        await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "again" } } });
        var listed = await fixture.RequestAsync("session/list", new { cwd = other });

        Assert.True(loaded.TryGetProperty("result", out _));
        Assert.Contains(listed.GetProperty("result").GetProperty("sessions").EnumerateArray(), entry => entry.GetProperty("sessionId").GetString() == id);
    }

    [Fact]
    public async Task List_PagesNewestFirstAndRejectsAForeignCursor()
    {
        await using var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false));
        var id = await fixture.OpenAsync();
        await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "hello list" } } });

        var listed = await fixture.RequestAsync("session/list", new { cwd = fixture.Workspace });
        var bad = await fixture.RequestAsync("session/list", new { cursor = "not-a-cursor" });

        var session = listed.GetProperty("result").GetProperty("sessions")[0];
        Assert.Equal(id, session.GetProperty("sessionId").GetString());
        Assert.Equal("hello list", session.GetProperty("title").GetString());
        Assert.True(Path.IsPathFullyQualified(session.GetProperty("cwd").GetString()!));
        Assert.Equal(1, session.GetProperty("_meta").GetProperty("crystal").GetProperty("userTurns").GetInt32());
        Assert.False(listed.GetProperty("result").TryGetProperty("nextCursor", out _));
        Assert.Equal(-32602, bad.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Clear_KeepsTheAcpIdAndReportsTheNewEngineId()
    {
        await using var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false));
        var id = await fixture.OpenAsync();

        await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "/clear" } } });
        var info = await fixture.WaitForUpdateAsync("session_info_update", static update =>
            update.GetProperty("_meta").GetProperty("crystal").GetProperty("event").GetString() == "ConversationCleared");
        var engineId = info.GetProperty("params").GetProperty("update").GetProperty("_meta").GetProperty("crystal")
            .GetProperty("engineSessionId").GetString();
        var byAcp = await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "hi" } } });
        var byEngine = await fixture.RequestAsync("session/prompt", new { sessionId = engineId, prompt = new[] { new { type = "text", text = "hi" } } });

        Assert.Equal(id, info.GetProperty("params").GetProperty("sessionId").GetString());
        Assert.NotEqual(id, engineId);
        Assert.True(byAcp.TryGetProperty("result", out _));
        Assert.True(byEngine.TryGetProperty("result", out _));
    }

    [Fact]
    public async Task Cd_ReportsTheNewWorkspace()
    {
        await using var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false));
        var other = Directory.CreateDirectory(Path.Combine(fixture.Root, "elsewhere")).FullName;
        new WorkspaceTrustStore(new CrystalHome(fixture.Home)).Remember(other);
        var id = await fixture.OpenAsync();

        await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "/cd " + other } } });

        var info = await fixture.WaitForUpdateAsync("session_info_update", static update =>
            update.GetProperty("_meta").GetProperty("crystal").GetProperty("event").GetString() == "ChromeChanged");
        Assert.EndsWith("elsewhere", info.GetProperty("params").GetProperty("update").GetProperty("_meta").GetProperty("crystal")
            .GetProperty("workspace").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SessionInfo_FollowsATurn()
    {
        await using var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false));
        var id = await fixture.OpenAsync();

        await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "title me" } } });

        var info = await fixture.WaitForUpdateAsync("session_info_update");
        var update = info.GetProperty("params").GetProperty("update");
        Assert.Equal("title me", update.GetProperty("title").GetString());
        Assert.True(DateTimeOffset.TryParse(update.GetProperty("updatedAt").GetString(), out _));
    }

    [Fact]
    public async Task Usage_IsReportedWithTheContextWindow()
    {
        var model = new ScriptedModel(false,
        [
            new Crystal.Chat.ChatTextDelta(0, 0, Crystal.Chat.ChatRole.Assistant, "ok"),
            new Crystal.Chat.ChatUsageReceived(new Crystal.TokenUsage(120, 30)),
            new Crystal.Chat.ChatCandidateCompleted(0, Crystal.FinishReason.Stop)
        ]);
        await using var fixture = await BridgeFixture.StartAsync(model);
        var id = await fixture.OpenAsync();

        await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "count" } } });

        var usage = fixture.Updates().Last(static update => update.GetProperty("sessionUpdate").GetString() == "usage_update"
            && update.GetProperty("_meta").GetProperty("crystal").GetProperty("event").GetString() == "UsageChanged");
        Assert.Equal(150, usage.GetProperty("used").GetInt64());
        Assert.True(usage.GetProperty("size").GetInt64() > 0);
    }

    [Fact]
    public async Task AdditionalDirectories_ReplaceOutsideRulesAndKeepNarrowingOnes()
    {
        await using var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false));
        var outside = Directory.CreateDirectory(Path.Combine(fixture.Root, "outside")).FullName;
        var other = Directory.CreateDirectory(Path.Combine(fixture.Root, "other")).FullName;
        var inside = Directory.CreateDirectory(Path.Combine(fixture.Workspace, "docs")).FullName;
        var store = new WorkspaceAccessStore(new CrystalHome(fixture.Home));
        Assert.True(store.TrySet(fixture.Workspace, new DirectoryAccess(inside, "read"), out _));
        Assert.True(store.TrySet(fixture.Workspace, new DirectoryAccess(other, "write"), out _));
        await fixture.RequestAsync("initialize", new { protocolVersion = 1 });

        var created = await fixture.RequestAsync("session/new", new { cwd = fixture.Workspace, mcpServers = Array.Empty<object>(), additionalDirectories = new[] { outside } });
        var id = created.GetProperty("result").GetProperty("sessionId").GetString()!;
        await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "hi" } } });
        var inWorkspace = await fixture.RequestAsync("session/new", new { cwd = fixture.Workspace, mcpServers = Array.Empty<object>(), additionalDirectories = new[] { inside } });
        var listed = await fixture.RequestAsync("session/list", new { cwd = fixture.Workspace });

        var document = JsonNode.Parse(File.ReadAllText(Path.Combine(fixture.Home, "sessions", id + ".json")))!;
        var access = document["access"]!.AsArray().Select(static rule => (Path: rule!["path"]!.GetValue<string>(), Mode: rule["mode"]!.GetValue<string>())).ToArray();
        Assert.Contains(access, rule => rule.Mode == "write" && rule.Path.EndsWith("outside", StringComparison.Ordinal));
        Assert.Contains(access, rule => rule.Mode == "read" && rule.Path.EndsWith("docs", StringComparison.Ordinal));
        Assert.DoesNotContain(access, rule => rule.Path.EndsWith("other", StringComparison.Ordinal));
        Assert.Equal(-32602, inWorkspace.GetProperty("error").GetProperty("code").GetInt32());
        var session = listed.GetProperty("result").GetProperty("sessions").EnumerateArray().Single(entry => entry.GetProperty("sessionId").GetString() == id);
        Assert.EndsWith("outside", session.GetProperty("additionalDirectories")[0].GetString(), StringComparison.Ordinal);
    }
}
