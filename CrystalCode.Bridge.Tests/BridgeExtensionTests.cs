using System.Text.Json;

using Crystal;
using Crystal.Chat;

using CrystalCode.Bridge.Tests.Support;
using CrystalCode.Engine.Home;

using Xunit;

namespace CrystalCode.Bridge.Tests;

/// <summary>Children, compaction, fork, and the <c>_crystal/*</c> extensions.</summary>
public sealed class BridgeExtensionTests
{
    private static readonly object Subagents = new { _meta = new { crystal = new { subagents = new { } } } };

    private static readonly object Notified = new { _meta = new { crystal = new { extensions = new { } } } };

    [Fact]
    public async Task Initialize_ListsEveryExtensionAndNotification()
    {
        await using var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false));

        var response = await fixture.RequestAsync("initialize", new { protocolVersion = 1 });

        var crystal = response.GetProperty("result").GetProperty("agentCapabilities").GetProperty("_meta").GetProperty("crystal");
        var extensions = crystal.GetProperty("extensions").EnumerateArray().Select(static value => value.GetString()).ToArray();
        Assert.Contains("_crystal/snapshot", extensions);
        Assert.Contains("_crystal/session/fork", extensions);
        Assert.Contains("_crystal/tasks/logs", extensions);
        Assert.Equal(29, extensions.Length);
        Assert.Contains("_crystal/queue", crystal.GetProperty("eventKinds").EnumerateArray().Select(static value => value.GetString()));
        Assert.Equal("rfd", crystal.GetProperty("fork").GetString());
        Assert.Equal("rfd", crystal.GetProperty("subagents").GetString());
        Assert.False(response.GetProperty("result").GetProperty("agentCapabilities").GetProperty("sessionCapabilities").TryGetProperty("fork", out _));
    }

    [Fact]
    public async Task Snapshot_HasTheServerFields()
    {
        await using var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false, ScriptedModel.TextRound("snap")));
        var id = await fixture.OpenAsync();
        await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "hi" } } });

        var snapshot = (await fixture.RequestAsync("_crystal/snapshot", new { sessionId = id })).GetProperty("result");

        foreach (var field in new[] { "chrome", "preferences", "commands", "promptHistory", "conversation", "live", "turnActive",
                     "activity", "queue", "todos", "statusFields", "usage", "cumulativeUsage", "contextWindow", "side", "children", "lastTurn" })
        {
            Assert.True(snapshot.TryGetProperty(field, out _), field);
        }

        Assert.Equal("completed", snapshot.GetProperty("lastTurn").GetProperty("reason").GetString());
        Assert.Contains(snapshot.GetProperty("conversation").EnumerateArray(), static item => item.GetProperty("text").GetString() == "snap");
    }

    [Fact]
    public async Task Extensions_NeedTheActiveSessionAndUnknownOnesAreMethodNotFound()
    {
        await using var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false));
        var id = await fixture.OpenAsync();

        var other = await fixture.RequestAsync("_crystal/snapshot", new { sessionId = "nope" });
        var unknown = await fixture.RequestAsync("_crystal/nope", new { sessionId = id });

        Assert.Equal(-32002, other.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(-32601, unknown.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Notifications_NeedTheOptIn()
    {
        await using var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false, ScriptedModel.TextRound("plain")));
        var id = await fixture.OpenAsync();

        await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "/todos" } } });
        var toggled = await fixture.RequestAsync("_crystal/mode/togglePlan", new { sessionId = id });

        Assert.Equal("plan", toggled.GetProperty("result").GetProperty("mode").GetString());
        lock (fixture.Lines)
        {
            Assert.DoesNotContain(fixture.Lines, static line => line.Contains("\"method\":\"_crystal/", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task ModeVerboseDraftAndRecall_ReachTheEngine()
    {
        await using var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false));
        var id = await fixture.OpenAsync(Notified);

        var toggled = await fixture.RequestAsync("_crystal/mode/togglePlan", new { sessionId = id });
        var cycled = await fixture.RequestAsync("_crystal/mode/cycle", new { sessionId = id });
        var verbose = await fixture.RequestAsync("_crystal/verbose", new { sessionId = id, target = "tools", enabled = false });
        var badVerbose = await fixture.RequestAsync("_crystal/verbose", new { sessionId = id, target = "nope", enabled = true });
        var draft = await fixture.RequestAsync("_crystal/draft", new { sessionId = id, text = "typing" });
        var recall = await fixture.RequestAsync("_crystal/recall", new { sessionId = id });

        Assert.Equal("plan", toggled.GetProperty("result").GetProperty("mode").GetString());
        Assert.Equal("work", cycled.GetProperty("result").GetProperty("mode").GetString());
        Assert.False(verbose.GetProperty("result").GetProperty("enabled").GetBoolean());
        Assert.Equal(-32602, badVerbose.GetProperty("error").GetProperty("code").GetInt32());
        Assert.True(draft.TryGetProperty("result", out _));
        Assert.False(recall.GetProperty("result").GetProperty("recalled").GetBoolean());
        await fixture.WaitForAsync(static message => Method(message) == "_crystal/preferences");
    }

    [Fact]
    public async Task TopicNaming_SendsItsActivityToAnOptedInClient()
    {
        await using var fixture = await BridgeFixture.StartAsync(
            new ScriptedModel(false, ScriptedModel.TextRound("done"), ScriptedModel.TextRound("Tidy the build")),
            options: static options => options with { Topic = "on" });
        var id = await fixture.OpenAsync(Notified);

        await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "clean the build" } } });

        await fixture.WaitForAsync(static message => Method(message) == "_crystal/activity"
            && message.GetProperty("params").GetProperty("activity").GetString() == "naming_topic");
        await fixture.WaitForAsync(static message => Method(message) == "_crystal/activity"
            && message.GetProperty("params").GetProperty("activity").GetString() == "idle");
        var snapshot = (await fixture.RequestAsync("_crystal/snapshot", new { sessionId = id })).GetProperty("result");
        Assert.Equal("Tidy the build", snapshot.GetProperty("chrome").GetProperty("topic").GetString());
        Assert.Equal("idle", snapshot.GetProperty("activity").GetString());
    }

    [Fact]
    public async Task Images_AttachAndGetBack()
    {
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
        await using var fixture = await BridgeFixture.StartAsync(
            new ScriptedModel(false) { Vision = new VisionModel() },
            options: static launch => launch with { Provider = "seen", Model = "eyes" },
            prepare: static (home, _) => File.WriteAllText(
                Path.Combine(home.Root, "providers.json"),
                """{"seen":{"protocol":"ollama","baseUri":"http://127.0.0.1:9/","models":{"eyes":{"contextWindow":8192,"imageInput":true}}}}"""));
        var id = await fixture.OpenAsync();

        var attached = (await fixture.RequestAsync("_crystal/image/attach", new { sessionId = id, data = Convert.ToBase64String(png) })).GetProperty("result");
        var number = attached.GetProperty("number").GetInt32();
        var fetched = (await fixture.RequestAsync("_crystal/image/get", new { sessionId = id, number })).GetProperty("result");
        var missing = await fixture.RequestAsync("_crystal/image/get", new { sessionId = id, number = 99 });
        var invalid = await fixture.RequestAsync("_crystal/image/attach", new { sessionId = id, data = Convert.ToBase64String("nope"u8.ToArray()) });

        Assert.Equal("image/png", attached.GetProperty("mimeType").GetString());
        Assert.StartsWith("\u2063", attached.GetProperty("trustedMarker").GetString(), StringComparison.Ordinal);
        Assert.Equal(png, fetched.GetProperty("data").GetBytesFromBase64());
        Assert.Equal(-32002, missing.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(-32602, invalid.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public void PlainMarker_IsTrustedOnlyForAnAttachedNumberOnce()
    {
        var attached = new HashSet<int> { 2 };

        var text = Prompts.PromptContent.TrustAttached("see [Image #2] and [Image #3] and [Image #2]", attached.Remove);

        Assert.Equal("see \u2063[Image #2] and [Image #3] and [Image #2]", text);
        Assert.Empty(attached);
    }

    [Fact]
    public async Task QuickPrompts_ListAndSend()
    {
        var model = new ScriptedModel(false, ScriptedModel.TextRound("quick done"));
        await using var fixture = await BridgeFixture.StartAsync(model, prepare: static (home, _) =>
        {
            var directory = Path.Combine(home.Root, "shortcuts");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "review.md"), "Review the diff.");
        });
        var id = await fixture.OpenAsync();

        var listed = (await fixture.RequestAsync("_crystal/quickPrompts/list", new { sessionId = id })).GetProperty("result");
        var sent = await fixture.RequestAsync("_crystal/quickPrompts/send", new { sessionId = id, name = "review" });
        var missing = await fixture.RequestAsync("_crystal/quickPrompts/send", new { sessionId = id, name = "nope" });

        Assert.Equal("review", listed.GetProperty("prompts")[0].GetProperty("name").GetString());
        Assert.True(sent.TryGetProperty("result", out _));
        Assert.Equal(-32002, missing.GetProperty("error").GetProperty("code").GetInt32());
        await fixture.WaitForUpdateAsync("agent_message_chunk", static update =>
            update.GetProperty("content").GetProperty("text").GetString() == "quick done");
    }

    [Fact]
    public async Task Catalogs_AnswerWithStructuredValues()
    {
        await using var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false));
        var id = await fixture.OpenAsync(Notified);

        var models = (await fixture.RequestAsync("_crystal/models", new { sessionId = id })).GetProperty("result");
        var agents = (await fixture.RequestAsync("_crystal/agents", new { sessionId = id })).GetProperty("result");
        var skills = (await fixture.RequestAsync("_crystal/skills", new { sessionId = id })).GetProperty("result");
        var mcp = (await fixture.RequestAsync("_crystal/mcp", new { sessionId = id })).GetProperty("result");
        var plugins = (await fixture.RequestAsync("_crystal/plugins", new { sessionId = id })).GetProperty("result");
        var tasks = (await fixture.RequestAsync("_crystal/tasks", new { sessionId = id })).GetProperty("result");

        var current = Assert.Single(models.GetProperty("models").EnumerateArray(), static model => model.GetProperty("current").GetBoolean());
        Assert.Equal("ollama", current.GetProperty("provider").GetString());
        Assert.False(current.TryGetProperty("baseUri", out _));
        Assert.Equal(JsonValueKind.Object, models.GetProperty("pending").ValueKind);
        Assert.Contains(agents.GetProperty("agents").EnumerateArray(), static agent => agent.GetProperty("name").GetString() == "general");
        Assert.False(skills.GetProperty("enabled").GetBoolean());
        Assert.False(mcp.GetProperty("enabled").GetBoolean());
        Assert.Equal(JsonValueKind.Array, plugins.GetProperty("plugins").ValueKind);
        Assert.Empty(tasks.GetProperty("tasks").EnumerateArray());
    }

    [Fact]
    public async Task TaskLogs_ReportAnUnknownTaskAndBadParameters()
    {
        await using var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false));
        var id = await fixture.OpenAsync(Notified);

        var missing = await fixture.RequestAsync("_crystal/tasks/logs", new { sessionId = id, taskId = "task_missing" });
        var badOffset = await fixture.RequestAsync("_crystal/tasks/logs", new { sessionId = id, taskId = "task_missing", offset = "x" });
        var noId = await fixture.RequestAsync("_crystal/tasks/logs", new { sessionId = id });

        Assert.Equal(-32002, missing.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(-32602, badOffset.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(-32602, noId.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Catalogs_NeverCarryAProviderKey()
    {
        const string secret = "sk-bridge-catalog-secret-1234";
        await using var fixture = await BridgeFixture.StartAsync(
            new ScriptedModel(false),
            prepare: (home, _) => File.WriteAllText(
                Path.Combine(home.Root, "providers.json"),
                $$"""
                {
                  "leaky": {
                    "protocol": "ollama",
                    "baseUri": "http://127.0.0.1:9/",
                    "apiKey": "{{secret}}",
                    "models": { "m": { "contextWindow": 8192 } }
                  }
                }
                """));
        var id = await fixture.OpenAsync(Notified);

        foreach (var method in new[] { "_crystal/models", "_crystal/agents", "_crystal/skills", "_crystal/mcp", "_crystal/plugins", "_crystal/tasks" })
        {
            var reply = await fixture.RequestAsync(method, new { sessionId = id });
            Assert.True(reply.TryGetProperty("result", out _), method);
        }

        Assert.All(fixture.Lines, line => Assert.DoesNotContain(secret, line, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reports_AnswerWithStructuredValues()
    {
        await using var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false));
        var id = await fixture.OpenAsync(Notified);

        var status = (await fixture.RequestAsync("_crystal/status", new { sessionId = id, full = true })).GetProperty("result");
        var tools = (await fixture.RequestAsync("_crystal/tools", new { sessionId = id })).GetProperty("result");
        var stats = (await fixture.RequestAsync("_crystal/stats", new { sessionId = id })).GetProperty("result");
        await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "/todos" } } });

        Assert.True(status.GetProperty("full").GetBoolean());
        Assert.Equal("ollama", status.GetProperty("status").GetProperty("provider").GetString());
        Assert.Contains(tools.GetProperty("report").GetProperty("work").EnumerateArray(), static tool => tool.GetProperty("name").GetString() == "read");
        Assert.True(stats.GetProperty("report").TryGetProperty("sessions", out _));
        var report = await fixture.WaitForAsync(static message => Method(message) == "_crystal/report"
            && message.GetProperty("params").GetProperty("kind").GetString() == "todos");
        Assert.Equal(0, report.GetProperty("params").GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task ExportAndSide_Work()
    {
        await using var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false, ScriptedModel.TextRound("exported text")));
        var id = await fixture.OpenAsync(Notified);
        await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "hi" } } });

        var markdown = (await fixture.RequestAsync("_crystal/export", new { sessionId = id, format = "markdown" })).GetProperty("result");
        var bad = await fixture.RequestAsync("_crystal/export", new { sessionId = id, format = "yaml" });
        var emptySide = await fixture.RequestAsync("_crystal/side/ask", new { sessionId = id, question = " " });
        var cancelled = (await fixture.RequestAsync("_crystal/side/cancel", new { sessionId = id })).GetProperty("result");
        var asked = await fixture.RequestAsync("_crystal/side/ask", new { sessionId = id, question = "what was that?" });
        var side = await fixture.WaitForAsync(static message => Method(message) == "_crystal/side");
        var cleared = await fixture.RequestAsync("_crystal/side/clear", new { sessionId = id });

        Assert.Contains("exported text", markdown.GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.EndsWith(".md", markdown.GetProperty("fileName").GetString(), StringComparison.Ordinal);
        Assert.Equal(-32602, bad.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(-32602, emptySide.GetProperty("error").GetProperty("code").GetInt32());
        Assert.False(cancelled.GetProperty("cancelled").GetBoolean());
        Assert.True(cleared.TryGetProperty("result", out _));
        Assert.True(asked.TryGetProperty("result", out _));
        Assert.True(side.GetProperty("params").GetProperty("side").TryGetProperty("exchanges", out _));
    }

    [Fact]
    public async Task QueueRelease_AndQueueNotification()
    {
        var model = new ScriptedModel(true);
        await using var fixture = await BridgeFixture.StartAsync(model);
        var id = await fixture.OpenAsync(Notified);
        var held = model.Held.Task;
        var (_, first) = await fixture.StartRequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "long" } } });
        await held.WaitAsync(BridgeFixture.Wait);
        var (_, second) = await fixture.StartRequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "later" } } });
        var queue = await fixture.WaitForAsync(static message => Method(message) == "_crystal/queue"
            && message.GetProperty("params").GetProperty("items").GetArrayLength() == 1);

        await fixture.RequestAsync("_crystal/queue/release", new { sessionId = id });
        Assert.Equal("cancelled", (await first.WaitAsync(BridgeFixture.Wait)).GetProperty("result").GetProperty("stopReason").GetString());
        await fixture.NotifyAsync("session/cancel", new { sessionId = id });
        await second.WaitAsync(BridgeFixture.Wait);

        Assert.Equal("later", queue.GetProperty("params").GetProperty("items")[0].GetString());
    }

    [Fact]
    public async Task Compaction_IsAnEntityWithTheCapability()
    {
        await using var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false,
            ScriptedModel.TextRound("first"),
            ScriptedModel.TextRound("second"),
            ScriptedModel.TextRound("Summary of earlier work.")));
        var id = await fixture.OpenAsync(new { session = new { compaction = new { } } });
        await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "one" } } });
        await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "two" } } });

        await fixture.RequestAsync("_crystal/compact", new { sessionId = id, full = true });

        var updates = fixture.Updates().Where(static update => update.GetProperty("sessionUpdate").GetString() is "compaction_update" or "compaction_summary_chunk").ToArray();
        Assert.Equal("in_progress", updates[0].GetProperty("status").GetString());
        var compactionId = updates[0].GetProperty("compactionId").GetString();
        Assert.All(updates, update => Assert.Equal(compactionId, update.GetProperty("compactionId").GetString()));
        Assert.Equal("completed", updates[^1].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Compaction_WithoutTheCapability_SendsNoCompactionUpdate()
    {
        await using var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false,
            ScriptedModel.TextRound("first"),
            ScriptedModel.TextRound("Summary of earlier work.")));
        var id = await fixture.OpenAsync();
        await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "one" } } });

        await fixture.RequestAsync("_crystal/compact", new { sessionId = id, full = true });

        Assert.DoesNotContain(fixture.Updates(), static update => update.GetProperty("sessionUpdate").GetString() is "compaction_update" or "compaction_summary_chunk");
    }

    [Fact]
    public async Task Fork_BranchesASavedSessionAndLeavesTheSourceAlone()
    {
        await using var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false, ScriptedModel.TextRound("origin"), ScriptedModel.TextRound("branch")));
        var id = await fixture.OpenAsync();
        await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "base" } } });
        var store = new SessionStore(new CrystalHome(fixture.Home));
        Assert.True(store.TryLoad(id, out var before));

        var forked = await fixture.RequestAsync("session/fork", new { sessionId = id, cwd = fixture.Workspace, mcpServers = Array.Empty<object>() });
        var fork = forked.GetProperty("result").GetProperty("sessionId").GetString()!;
        await fixture.RequestAsync("session/prompt", new { sessionId = fork, prompt = new[] { new { type = "text", text = "more" } } });
        var byExtension = await fixture.RequestAsync("_crystal/session/fork", new { sessionId = id, cwd = fixture.Workspace });

        Assert.NotEqual(id, fork);
        Assert.True(forked.GetProperty("result").TryGetProperty("configOptions", out _));
        Assert.True(store.TryLoad(id, out var after));
        Assert.Equal(before.UserTurns, after.UserTurns);
        Assert.Equal(before.Items.Count, after.Items.Count);
        Assert.True(store.TryLoad(fork, out var branch));
        Assert.Equal(2, branch.UserTurns);
        Assert.NotEqual(fork, byExtension.GetProperty("result").GetProperty("sessionId").GetString());
        await fixture.WaitForUpdateAsync("user_message_chunk", static update =>
            update.GetProperty("content").GetProperty("text").GetString() == "base");
    }

    [Fact]
    public async Task Children_WithoutTheOptIn_UpdateTheParentTaskCall()
    {
        var model = new ScriptedModel(false,
            ScriptedModel.ToolRound("task-1", "task", """{"agent":"general","label":"look around","prompt":"Say hi"}"""),
            ScriptedModel.TextRound("child says hi"),
            ScriptedModel.TextRound("parent done"));
        await using var fixture = await BridgeFixture.StartAsync(model, options: static launch => launch with { Agents = "on", Approval = "full" });
        var id = await fixture.OpenAsync();

        await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "delegate" } } });

        lock (fixture.Lines)
        {
            Assert.DoesNotContain(fixture.Lines, static line => line.Contains("subagent_update", StringComparison.Ordinal));
        }

        var task = fixture.Updates().Where(static update => update.TryGetProperty("toolCallId", out var call) && call.GetString() == "task-1").ToArray();
        Assert.Contains(task, static update => update.TryGetProperty("_meta", out var meta)
            && meta.GetProperty("crystal").TryGetProperty("child", out var child)
            && child.GetProperty("label").GetString() == "look around");
        Assert.Contains(task, static update => update.TryGetProperty("content", out var content)
            && content[0].GetProperty("content").GetProperty("text").GetString() == "child says hi");
        var children = (await fixture.RequestAsync("_crystal/children/list", new { sessionId = id })).GetProperty("result").GetProperty("children");
        var childId = children[0].GetProperty("id").GetString();
        Assert.All(fixture.Lines.Where(static line => line.Contains("\"session/update\"", StringComparison.Ordinal)), line =>
            Assert.DoesNotContain("\"sessionId\":\"" + childId + "\"", line, StringComparison.Ordinal));
        var full = (await fixture.RequestAsync("_crystal/children/get", new { sessionId = id, childSessionId = childId })).GetProperty("result");
        Assert.True(full.GetProperty("child").TryGetProperty("archive", out _));
    }

    [Fact]
    public async Task Children_WithTheOptIn_AreAnnouncedSessions()
    {
        var model = new ScriptedModel(false,
            ScriptedModel.ToolRound("task-1", "task", """{"agent":"general","label":"look around","prompt":"Say hi"}"""),
            ScriptedModel.TextRound("child says hi"),
            ScriptedModel.TextRound("parent done"));
        await using var fixture = await BridgeFixture.StartAsync(model, options: static launch => launch with { Agents = "on", Approval = "full" });
        var id = await fixture.OpenAsync(Subagents);

        await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "delegate" } } });

        var lines = fixture.Lines.Select(static line => JsonDocument.Parse(line).RootElement).ToArray();
        var announce = Array.FindIndex(lines, static message => Method(message) == "session/update"
            && message.GetProperty("params").GetProperty("update").GetProperty("sessionUpdate").GetString() == "subagent_update");
        Assert.True(announce >= 0);
        var announcement = lines[announce].GetProperty("params");
        Assert.Equal(id, announcement.GetProperty("sessionId").GetString());
        var childId = announcement.GetProperty("update").GetProperty("sessionId").GetString();
        Assert.Equal("look around", announcement.GetProperty("update").GetProperty("title").GetString());
        var firstChild = Array.FindIndex(lines, message => Method(message) == "session/update"
            && message.GetProperty("params").GetProperty("sessionId").GetString() == childId);
        Assert.True(firstChild > announce);
        Assert.Contains(lines, message => Method(message) == "session/update"
            && message.GetProperty("params").GetProperty("sessionId").GetString() == childId
            && message.GetProperty("params").GetProperty("update").GetProperty("sessionUpdate").GetString() == "agent_message_chunk");
        Assert.Contains(lines, static message => Method(message) == "session/update"
            && message.GetProperty("params").GetProperty("update").GetProperty("sessionUpdate").GetString() == "subagent_update"
            && message.GetProperty("params").GetProperty("update").GetProperty("state").GetProperty("state").GetString() == "idle");
    }

    [Fact]
    public async Task ChildApproval_WithoutTheOptIn_UsesTheParentSessionAndTaskCall()
    {
        var model = new ScriptedModel(false,
            ScriptedModel.ToolRound("task-1", "task", """{"agent":"general","label":"runner","prompt":"Run it"}"""),
            ScriptedModel.ToolRound("child-call", "bash", """{"command":"echo child"}"""),
            ScriptedModel.TextRound("child done"),
            ScriptedModel.TextRound("parent done"));
        await using var fixture = await BridgeFixture.StartAsync(model, options: static launch => launch with { Agents = "on" });
        fixture.OnRequest = static (method, _) => method == "session/request_permission" ? BridgeProtocolTests.Selected("once") : null;
        var id = await fixture.OpenAsync();

        await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "delegate" } } });

        var request = (await fixture.WaitForAsync(static message => Method(message) == "session/request_permission"
            && message.GetProperty("params").GetProperty("_meta").GetProperty("crystal").TryGetProperty("child", out _))).GetProperty("params");
        Assert.Equal(id, request.GetProperty("sessionId").GetString());
        Assert.Equal("task-1", request.GetProperty("toolCall").GetProperty("toolCallId").GetString());
        Assert.StartsWith("Child runner: ", request.GetProperty("toolCall").GetProperty("title").GetString(), StringComparison.Ordinal);
        var child = request.GetProperty("_meta").GetProperty("crystal").GetProperty("child");
        Assert.Equal("child-call", child.GetProperty("toolCallId").GetString());
        Assert.DoesNotContain(fixture.Updates(), static update => update.TryGetProperty("toolCallId", out var call) && call.GetString() == "child-call");
    }

    [Fact]
    public async Task ChildApproval_WithTheOptIn_UsesTheChildSession()
    {
        var model = new ScriptedModel(false,
            ScriptedModel.ToolRound("task-1", "task", """{"agent":"general","label":"runner","prompt":"Run it"}"""),
            ScriptedModel.ToolRound("child-call", "bash", """{"command":"echo child"}"""),
            ScriptedModel.TextRound("child done"),
            ScriptedModel.TextRound("parent done"));
        await using var fixture = await BridgeFixture.StartAsync(model, options: static launch => launch with { Agents = "on" });
        fixture.OnRequest = static (method, _) => method == "session/request_permission" ? BridgeProtocolTests.Selected("once") : null;
        var id = await fixture.OpenAsync(Subagents);

        await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "delegate" } } });

        var request = (await fixture.WaitForAsync(static message => Method(message) == "session/request_permission"
            && message.GetProperty("params").GetProperty("toolCall").GetProperty("toolCallId").GetString() == "child-call")).GetProperty("params");
        Assert.NotEqual(id, request.GetProperty("sessionId").GetString());
        var childId = request.GetProperty("sessionId").GetString();
        Assert.Contains(fixture.Lines.Select(static line => JsonDocument.Parse(line).RootElement), message =>
            Method(message) == "session/update"
            && message.GetProperty("params").GetProperty("sessionId").GetString() == childId
            && message.GetProperty("params").GetProperty("update").TryGetProperty("toolCallId", out var call)
            && call.GetString() == "child-call");
    }

    [Fact]
    public async Task RootSubagentsCapability_IsNotRead()
    {
        var model = new ScriptedModel(false,
            ScriptedModel.ToolRound("task-1", "task", """{"agent":"general","label":"x","prompt":"Say hi"}"""),
            ScriptedModel.TextRound("hi"),
            ScriptedModel.TextRound("done"));
        await using var fixture = await BridgeFixture.StartAsync(model, options: static launch => launch with { Agents = "on", Approval = "full" });
        var id = await fixture.OpenAsync(new { subagents = new { } });

        await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "delegate" } } });

        lock (fixture.Lines)
        {
            Assert.DoesNotContain(fixture.Lines, static line => line.Contains("subagent_update", StringComparison.Ordinal));
        }
    }

    private static string? Method(JsonElement message) =>
        message.TryGetProperty("method", out var method) ? method.GetString() : null;
}
