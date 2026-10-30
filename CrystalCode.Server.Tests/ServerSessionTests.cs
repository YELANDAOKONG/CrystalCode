using System.Net;
using System.Text.Json;

using Crystal;
using Crystal.Chat;
using Crystal.Tools;

using CrystalCode.Server.Tests.Support;

using Xunit;

namespace CrystalCode.Server.Tests;

public sealed class ServerSessionTests
{
    [Fact]
    public async Task Turn_StreamsEventsAndSnapshotHoldsTheConversation()
    {
        await using var fixture = await ServerFixture.StartAsync(new ScriptedModel(false, TextRound("Hello there.")));
        await fixture.WaitReadyAsync();
        using var events = await fixture.OpenEventsAsync(fixture.Http);

        var accepted = await fixture.PostAsync(fixture.Http, "/input", new { text = "hi" });
        await events.WaitForAsync("turn.finished");
        var snapshot = await fixture.GetAsync(fixture.Http, "/snapshot");

        Assert.True(accepted.GetProperty("turnActive").GetBoolean());
        var types = events.Types();
        Assert.Contains("message.sent", types);
        Assert.Contains("stream.text", types);
        Assert.True(types.ToList().IndexOf("turn.started") < types.ToList().IndexOf("turn.finished"));
        var conversation = snapshot.GetProperty("conversation").EnumerateArray().ToArray();
        Assert.Equal("hi", conversation[0].GetProperty("text").GetString());
        Assert.Equal("Hello there.", conversation[^1].GetProperty("text").GetString());
        Assert.True(snapshot.GetProperty("lastSeq").GetInt64() > 0);
        Assert.Equal("ready", snapshot.GetProperty("state").GetString());
        Assert.NotEmpty(snapshot.GetProperty("commands").EnumerateArray());
    }

    [Fact]
    public async Task Events_ResumeAfterASequenceWithoutRepeats()
    {
        await using var fixture = await ServerFixture.StartAsync(new ScriptedModel(false, TextRound("one"), TextRound("two")));
        await fixture.WaitReadyAsync();
        using (var first = await fixture.OpenEventsAsync(fixture.Http))
        {
            await fixture.PostAsync(fixture.Http, "/input", new { text = "a" });
            await first.WaitForAsync("turn.finished");
        }

        var snapshot = await fixture.GetAsync(fixture.Http, "/snapshot");
        var cursor = snapshot.GetProperty("lastSeq").GetInt64();
        using var second = await fixture.OpenEventsAsync(fixture.Http, cursor);
        await fixture.PostAsync(fixture.Http, "/input", new { text = "b" });
        await second.WaitForAsync("turn.finished");

        lock (second.Seen)
        {
            Assert.All(second.Seen, value => Assert.True(value.GetProperty("seq").GetInt64() > cursor));
        }

        Assert.DoesNotContain(second.Seen, value => value.GetProperty("type").GetString() == "session.started");
    }

    [Fact]
    public async Task Chrome_NamesTheProvider()
    {
        await using var fixture = await ServerFixture.StartAsync(new ScriptedModel(false));
        await fixture.WaitReadyAsync();
        using var events = await fixture.OpenEventsAsync(fixture.Http);

        await fixture.PostAsync(fixture.Http, "/mode", new { action = "cycle" });
        var changed = await events.WaitForAsync("chrome.changed");

        var chrome = changed.GetProperty("data").GetProperty("chrome");
        Assert.Equal("ollama", chrome.GetProperty("provider").GetString());
        Assert.False(string.IsNullOrEmpty(chrome.GetProperty("sessionId").GetString()));
    }

    [Fact]
    public async Task Mcp_KeepsADisabledServerListedSoItCanBeEnabledAgain()
    {
        await using var fixture = await ServerFixture.StartAsync(
            new ScriptedModel(false),
            prepare: static (home, _) => File.WriteAllText(
                Path.Combine(home.Root, "mcp.json"),
                """{"mcpServers":{"tools":{"command":"crystal-no-such-command-for-tests","enabled":false}}}"""));
        await fixture.WaitReadyAsync();

        var listed = await fixture.GetAsync(fixture.Http, "/mcp");
        var server = Assert.Single(listed.GetProperty("servers").EnumerateArray());
        Assert.Equal("tools", server.GetProperty("name").GetString());
        Assert.Equal("disabled", server.GetProperty("status").GetString());
        Assert.False(server.GetProperty("enabled").GetBoolean());
        Assert.True(server.GetProperty("effective").GetBoolean());
        Assert.False(server.TryGetProperty("command", out _));
    }

    [Fact]
    public async Task SideModelTuning_IsAnOptionAndPartOfChrome()
    {
        await using var fixture = await ServerFixture.StartAsync(
            new ScriptedModel(false),
            prepare: (home, _) => File.WriteAllText(
                Path.Combine(home.Root, "providers.json"),
                """
                {
                  "side": {
                    "protocol": "ollama",
                    "baseUri": "http://127.0.0.1:9/",
                    "models": { "tuned": { "contextWindow": 8192, "serviceTiers": ["flex"], "speeds": ["fast"] } }
                  }
                }
                """));
        await fixture.WaitReadyAsync();
        using var events = await fixture.OpenEventsAsync(fixture.Http);

        await PutAsync(fixture, fixture.Http, "/options/approval_model", new { value = "side/tuned" });
        var speed = await PutAsync(fixture, fixture.Http, "/options/approval_speed", new { value = "fast" });
        var changed = await events.WaitForAsync(
            "chrome.changed",
            static value => value.GetProperty("data").GetProperty("chrome").GetProperty("reviewer") is { ValueKind: JsonValueKind.Object } reviewer
                && reviewer.GetProperty("speed").GetString() == "Speed Fast");

        Assert.Equal("fast", Option(speed, "approval_speed").GetProperty("current").GetString());
        var chrome = changed.GetProperty("data").GetProperty("chrome");
        Assert.Equal(JsonValueKind.Null, chrome.GetProperty("compaction").ValueKind);
        Assert.Equal(JsonValueKind.Null, chrome.GetProperty("image").ValueKind);
        Assert.Equal(JsonValueKind.Null, chrome.GetProperty("audio").ValueKind);
        Assert.Equal(JsonValueKind.Null, chrome.GetProperty("video").ValueKind);
    }

    [Fact]
    public async Task Reports_GoOnlyToTheRequestingClient()
    {
        await using var fixture = await ServerFixture.StartAsync(new ScriptedModel(false));
        await fixture.WaitReadyAsync();
        using var alice = fixture.Client("alice");
        using var bob = fixture.Client("bob");
        using var aliceEvents = await fixture.OpenEventsAsync(alice);
        using var bobEvents = await fixture.OpenEventsAsync(bob);

        await fixture.PostAsync(alice, "/input", new { text = "/status" });
        await fixture.PostAsync(bob, "/input", new { text = "/help" });
        await aliceEvents.WaitForAsync("report.status");
        await bobEvents.WaitForAsync("report.help");
        await fixture.PostAsync(alice, "/mode", new { action = "cycle" });
        await bobEvents.WaitForAsync("chrome.changed");

        Assert.DoesNotContain("report.help", aliceEvents.Types());
        Assert.DoesNotContain("report.status", bobEvents.Types());
    }

    [Fact]
    public async Task TopicNaming_ReachesEveryClientEvenWhenOneAskedForIt()
    {
        var model = new ScriptedModel(false, TextRound("done"), TextRound("Tidy the build"), TextRound("Repair the build"));
        await using var fixture = await ServerFixture.StartAsync(model, options: static launch => launch with { Topic = "on" });
        await fixture.WaitReadyAsync();
        using var alice = fixture.Client("alice");
        using var bob = fixture.Client("bob");
        using var aliceEvents = await fixture.OpenEventsAsync(alice);
        using var bobEvents = await fixture.OpenEventsAsync(bob);

        await fixture.PostAsync(alice, "/input", new { text = "clean the build" });
        await bobEvents.WaitForAsync("activity.changed", static value =>
            value.GetProperty("data").GetProperty("activity").GetString() == "naming_topic");
        await bobEvents.WaitForAsync("chrome.changed", static value =>
            value.GetProperty("data").GetProperty("chrome").GetProperty("topic").GetString() == "Tidy the build");

        await fixture.PostAsync(alice, "/input", new { text = "/topic regenerate" });
        var note = await bobEvents.WaitForAsync("note", static value =>
            value.GetProperty("data").GetProperty("text").GetString() == "Topic  Repair the build");
        Assert.Equal("note", note.GetProperty("type").GetString());
        await aliceEvents.WaitForAsync("note", static value =>
            value.GetProperty("data").GetProperty("text").GetString() == "Topic  Repair the build");

        var sessions = await fixture.GetAsync(bob, "/sessions");
        Assert.Equal("Repair the build", sessions.GetProperty("sessions")[0].GetProperty("topic").GetString());
        var snapshot = await fixture.GetAsync(bob, "/snapshot");
        Assert.Equal("Repair the build", snapshot.GetProperty("chrome").GetProperty("topic").GetString());
    }

    [Fact]
    public async Task Snapshot_KeepsApprovalPassesAfterTheTurn()
    {
        var model = new ScriptedModel(false, ToolRound("call-1", "list", "{}"), TextRound("done"));
        await using var fixture = await ServerFixture.StartAsync(model);
        await fixture.WaitReadyAsync();
        using var events = await fixture.OpenEventsAsync(fixture.Http);

        await fixture.PostAsync(fixture.Http, "/input", new { text = "look" });
        await events.WaitForAsync("approval.passed");
        await events.WaitForAsync("turn.finished");
        var snapshot = await fixture.GetAsync(fixture.Http, "/snapshot");

        var passed = Assert.Single(snapshot.GetProperty("approvals").EnumerateArray());
        Assert.Equal("call-1", passed.GetProperty("call").GetProperty("callId").GetString());
        Assert.Equal("policy", passed.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Queries_ListModelsOptionsSessionsAndCatalogsAsData()
    {
        var model = new ScriptedModel(false, TextRound("saved"));
        await using var fixture = await ServerFixture.StartAsync(model);
        await fixture.WaitReadyAsync();
        using var events = await fixture.OpenEventsAsync(fixture.Http);
        await fixture.PostAsync(fixture.Http, "/input", new { text = "hi" });
        await events.WaitForAsync("turn.finished");

        var models = await fixture.GetAsync(fixture.Http, "/models");
        var options = await fixture.GetAsync(fixture.Http, "/options");
        var sessions = await fixture.GetAsync(fixture.Http, "/sessions");
        var agents = await fixture.GetAsync(fixture.Http, "/agents");
        var skills = await fixture.GetAsync(fixture.Http, "/skills");
        var mcp = await fixture.GetAsync(fixture.Http, "/mcp");
        var plugins = await fixture.GetAsync(fixture.Http, "/plugins");
        var tasks = await fixture.GetAsync(fixture.Http, "/tasks");
        var missing = await fixture.Http.GetAsync("/tasks/task_missing/logs");

        var current = Assert.Single(models.GetProperty("models").EnumerateArray(), static entry => entry.GetProperty("current").GetBoolean());
        Assert.False(current.TryGetProperty("baseUri", out _));
        Assert.Contains(options.GetProperty("options").EnumerateArray(), static option => option.GetProperty("id").GetString() == "approval");
        Assert.Contains(sessions.GetProperty("sessions").EnumerateArray(), static saved =>
            saved.GetProperty("current").GetBoolean() && saved.GetProperty("open").GetBoolean());
        Assert.Contains(agents.GetProperty("agents").EnumerateArray(), static agent => agent.GetProperty("name").GetString() == "general");
        Assert.False(skills.GetProperty("enabled").GetBoolean());
        Assert.False(mcp.GetProperty("enabled").GetBoolean());
        Assert.Equal(JsonValueKind.Array, plugins.GetProperty("plugins").ValueKind);
        Assert.Empty(tasks.GetProperty("tasks").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task PutOption_RunsTheOptionCommandAndAppliesNextTurnWhileBusy()
    {
        var model = new ScriptedModel(true);
        await using var fixture = await ServerFixture.StartAsync(model);
        await fixture.WaitReadyAsync();

        var idle = await PutAsync(fixture, fixture.Http, "/options/approval", new { value = "edit" });
        var unknown = await fixture.Http.PutAsync("/options/nope", ServerFixture.Body(new { value = "x" }));
        var unoffered = await fixture.Http.PutAsync("/options/approval", ServerFixture.Body(new { value = "sometimes" }));
        var switched = await PutAsync(fixture, fixture.Http, "/options/estimated_tokens", new { value = true });
        await fixture.PostAsync(fixture.Http, "/input", new { text = "long task" });
        await model.Held.Task.WaitAsync(ServerFixture.Wait);
        var busy = await PutAsync(fixture, fixture.Http, "/options/mode", new { value = "plan" });

        Assert.False(idle.GetProperty("pending").GetBoolean());
        Assert.Equal("edit", Option(idle, "approval").GetProperty("current").GetString());
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unoffered.StatusCode);
        Assert.Equal("on", Option(switched, "estimated_tokens").GetProperty("current").GetString());
        Assert.True(busy.GetProperty("pending").GetBoolean());
        Assert.Equal("work", Option(busy, "mode").GetProperty("current").GetString());
        Assert.Equal("plan", busy.GetProperty("pendingSettings").GetProperty("mode").GetString());
        await fixture.PostAsync(fixture.Http, "/interrupt", new { });
    }

    [Fact]
    public async Task PutOption_KeepsTheCommandScopeAndReportsARefusal()
    {
        var model = new ScriptedModel(true);
        await using var fixture = await ServerFixture.StartAsync(model);
        await fixture.WaitReadyAsync();
        var issued = await fixture.PostAsync(fixture.Http, "/auth/tokens", new { scope = "operate" });
        using var work = fixture.Client("work", issued.GetProperty("token").GetString());

        var approval = await work.PutAsync("/options/approval", ServerFixture.Body(new { value = "full" }));
        var mode = await work.PutAsync("/options/mode", ServerFixture.Body(new { value = "plan" }));
        await fixture.PostAsync(fixture.Http, "/input", new { text = "long task" });
        await model.Held.Task.WaitAsync(ServerFixture.Wait);
        var refused = await fixture.Http.PutAsync("/options/prompt_set", ServerFixture.Body(new { value = "default" }));

        Assert.Equal(HttpStatusCode.Forbidden, approval.StatusCode);
        Assert.Equal(HttpStatusCode.OK, mode.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("Finish the current turn", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await fixture.PostAsync(fixture.Http, "/interrupt", new { });
    }

    private static async Task<JsonElement> PutAsync(ServerFixture fixture, HttpClient http, string path, object body)
    {
        var response = await http.PutAsync(path, ServerFixture.Body(body));
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, (int)response.StatusCode + ": " + text);
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static JsonElement Option(JsonElement result, string id) =>
        result.GetProperty("options").EnumerateArray().Single(option => option.GetProperty("id").GetString() == id);

    [Fact]
    public async Task Queries_NeedTheObserveScope()
    {
        await using var fixture = await ServerFixture.StartAsync(new ScriptedModel(false));
        await fixture.WaitReadyAsync();
        using var none = new HttpClient { BaseAddress = fixture.Http.BaseAddress };

        var refused = await none.GetAsync("/models");

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    [Fact]
    public async Task Approval_IsAnsweredByAClientAndRunsTheTool()
    {
        var model = new ScriptedModel(false, ToolRound("call-1", "bash", """{"command":"echo served"}"""), TextRound("done"));
        await using var fixture = await ServerFixture.StartAsync(model);
        await fixture.WaitReadyAsync();
        using var events = await fixture.OpenEventsAsync(fixture.Http);

        await fixture.PostAsync(fixture.Http, "/input", new { text = "run it" });
        var opened = await events.WaitForAsync("interaction.opened");
        var interaction = opened.GetProperty("data").GetProperty("interaction");
        Assert.Equal("approval", interaction.GetProperty("kind").GetString());
        var payload = interaction.GetProperty("payload");
        Assert.Equal("bash", payload.GetProperty("call").GetProperty("name").GetString());
        Assert.Contains("deny", payload.GetProperty("choices").EnumerateArray().Select(static value => value.GetString()));
        var id = interaction.GetProperty("id").GetString();

        var invalid = await fixture.Http.PostAsync("/interactions/" + id, ServerFixture.Body(new { choice = "maybe" }));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        await fixture.PostAsync(fixture.Http, "/interactions/" + id, new { choice = "once" });
        var late = await fixture.Http.PostAsync("/interactions/" + id, ServerFixture.Body(new { choice = "deny" }));
        var results = await events.WaitForAsync("tools.results");
        await events.WaitForAsync("turn.finished");

        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        var result = results.GetProperty("data").GetProperty("results")[0];
        Assert.Equal("success", result.GetProperty("status").GetString());
        Assert.Contains("served", result.GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.Contains("interaction.resolved", events.Types());
    }

    [Fact]
    public async Task Question_TakesLabelsAndRejectsUnofferedOnes()
    {
        var question = """
            {"questions":[{"header":"Pick","question":"Which?","options":[{"label":"A","description":"a"},{"label":"B","description":"b"}],"custom":false}]}
            """;
        var model = new ScriptedModel(false, ToolRound("q-1", "question", question), TextRound("thanks"));
        await using var fixture = await ServerFixture.StartAsync(model);
        await fixture.WaitReadyAsync();
        using var events = await fixture.OpenEventsAsync(fixture.Http);

        await fixture.PostAsync(fixture.Http, "/input", new { text = "ask me" });
        var opened = await events.WaitForAsync("interaction.opened");
        var id = opened.GetProperty("data").GetProperty("interaction").GetProperty("id").GetString();
        var wrong = await fixture.Http.PostAsync("/interactions/" + id, ServerFixture.Body(new { answers = new[] { new[] { "C" } } }));
        await fixture.PostAsync(fixture.Http, "/interactions/" + id, new { answers = new[] { new[] { "B" } } });
        var results = await events.WaitForAsync("tools.results");

        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Contains("B", results.GetProperty("data").GetProperty("results")[0].GetProperty("text").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Trust_IsAskedBeforeTheSessionOpens()
    {
        await using var fixture = await ServerFixture.StartAsync(new ScriptedModel(false), trusted: false);
        using var events = await fixture.OpenEventsAsync(fixture.Http);
        var opened = await events.WaitForAsync("interaction.opened");
        var health = await fixture.GetAsync(fixture.Http, "/health");
        Assert.Equal("awaiting_operator", health.GetProperty("state").GetString());

        var interaction = opened.GetProperty("data").GetProperty("interaction");
        Assert.Equal("trust", interaction.GetProperty("kind").GetString());
        await fixture.PostAsync(fixture.Http, "/interactions/" + interaction.GetProperty("id").GetString(), new { trust = true });
        await fixture.WaitReadyAsync();

        var ready = await fixture.GetAsync(fixture.Http, "/health");
        Assert.Equal("ready", ready.GetProperty("state").GetString());
    }

    [Fact]
    public async Task TrustDeclined_EndsTheServerWithAnError()
    {
        await using var fixture = await ServerFixture.StartAsync(new ScriptedModel(false), trusted: false);
        using var events = await fixture.OpenEventsAsync(fixture.Http);
        var opened = await events.WaitForAsync("interaction.opened");

        await fixture.PostAsync(fixture.Http, "/interactions/" + opened.GetProperty("data").GetProperty("interaction").GetProperty("id").GetString(), new { trust = false });

        Assert.Equal(1, await fixture.ExitAsync());
        Assert.Contains("declined", fixture.Error, StringComparison.Ordinal);
        var stopped = StoppedLine(fixture);
        Assert.Equal(1, stopped.GetProperty("exitCode").GetInt32());
        Assert.Equal("failed", stopped.GetProperty("reason").GetString());
        Assert.Contains("declined", stopped.GetProperty("error").GetString(), StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Null, stopped.GetProperty("sessionId").ValueKind);
    }

    [Fact]
    public async Task Interrupt_StopsARunningTurnAndQueueFollowsUp()
    {
        var model = new ScriptedModel(true);
        await using var fixture = await ServerFixture.StartAsync(model);
        await fixture.WaitReadyAsync();
        using var events = await fixture.OpenEventsAsync(fixture.Http);

        await fixture.PostAsync(fixture.Http, "/input", new { text = "long task" });
        await model.Held.Task.WaitAsync(ServerFixture.Wait);
        var queued = await fixture.PostAsync(fixture.Http, "/input", new { text = "later" });
        await events.WaitForAsync("queue.changed", value => value.GetProperty("data").GetProperty("items").GetArrayLength() == 1);
        var interrupted = await fixture.PostAsync(fixture.Http, "/interrupt", new { });
        var finished = await events.WaitForAsync("turn.finished");

        Assert.True(queued.GetProperty("turnActive").GetBoolean());
        Assert.True(interrupted.GetProperty("interrupted").GetBoolean());
        Assert.Equal("interrupted", finished.GetProperty("data").GetProperty("reason").GetString());
        await events.WaitForAsync("message.sent", value => value.GetProperty("data").GetProperty("text").GetString() == "later");
    }

    [Fact]
    public async Task Input_DuringATurnReportsPendingSettingsAndRefusals()
    {
        var model = new ScriptedModel(true);
        await using var fixture = await ServerFixture.StartAsync(model);
        await fixture.WaitReadyAsync();
        using var events = await fixture.OpenEventsAsync(fixture.Http);

        await fixture.PostAsync(fixture.Http, "/input", new { text = "long task" });
        await model.Held.Task.WaitAsync(ServerFixture.Wait);
        var plan = await fixture.PostAsync(fixture.Http, "/input", new { text = "/approval edit" });
        var refused = await fixture.PostAsync(fixture.Http, "/input", new { text = "/cd .." });
        var mode = await fixture.PostAsync(fixture.Http, "/mode", new { action = "set", name = "plan" });
        var snapshot = await fixture.GetAsync(fixture.Http, "/snapshot");

        Assert.True(plan.GetProperty("accepted").GetBoolean());
        Assert.True(plan.GetProperty("pending").GetBoolean());
        Assert.Equal(JsonValueKind.Null, plan.GetProperty("error").ValueKind);
        Assert.False(refused.GetProperty("accepted").GetBoolean());
        Assert.Contains("Finish the current turn", refused.GetProperty("error").GetString(), StringComparison.Ordinal);
        Assert.Equal("work", mode.GetProperty("mode").GetString());
        Assert.Equal("plan", mode.GetProperty("pending").GetProperty("mode").GetString());
        var chrome = snapshot.GetProperty("chrome");
        Assert.Equal("default", chrome.GetProperty("approval").GetString());
        Assert.Equal("edit", chrome.GetProperty("pending").GetProperty("approval").GetString());
        Assert.Equal("plan", chrome.GetProperty("pending").GetProperty("mode").GetString());

        await fixture.PostAsync(fixture.Http, "/interrupt", new { });
        await events.WaitForAsync("chrome.changed", value =>
            value.GetProperty("data").GetProperty("chrome").GetProperty("approval").GetString() == "edit"
            && value.GetProperty("data").GetProperty("chrome").GetProperty("mode").GetString() == "plan"
            && !value.GetProperty("data").GetProperty("chrome").GetProperty("pending").EnumerateObject().Any());
    }

    [Fact]
    public async Task Recall_ReturnsTheMessageToItsSender()
    {
        var model = new ScriptedModel(true) { Silent = true };
        await using var fixture = await ServerFixture.StartAsync(model);
        await fixture.WaitReadyAsync();
        using var alice = fixture.Client("alice");
        using var events = await fixture.OpenEventsAsync(alice);

        await fixture.PostAsync(alice, "/input", new { text = "oops" });
        await model.Held.Task.WaitAsync(ServerFixture.Wait);
        var recalled = await fixture.PostAsync(alice, "/recall", new { });

        Assert.True(recalled.GetProperty("recalled").GetBoolean());
        var message = await events.WaitForAsync("message.recalled");
        Assert.Equal("alice", message.GetProperty("origin").GetString());
        Assert.Equal("oops", message.GetProperty("data").GetProperty("draft").GetString());
    }

    [Fact]
    public async Task QuickPrompt_SendsTheTemplate()
    {
        await using var fixture = await ServerFixture.StartAsync(
            new ScriptedModel(false, TextRound("reviewed")),
            prepare: (home, _) =>
            {
                Directory.CreateDirectory(home.ShortcutsDirectory);
                File.WriteAllText(Path.Combine(home.ShortcutsDirectory, "review.md"), "Review the diff");
            });
        await fixture.WaitReadyAsync();
        using var events = await fixture.OpenEventsAsync(fixture.Http);

        var catalog = await fixture.GetAsync(fixture.Http, "/quick-prompts");
        Assert.Equal("review", catalog.GetProperty("prompts")[0].GetProperty("name").GetString());
        await fixture.PostAsync(fixture.Http, "/quick-prompts/review/send", new { });
        var sent = await events.WaitForAsync("message.sent");
        var missing = await fixture.Http.PostAsync("/quick-prompts/missing/send", ServerFixture.Body(new { }));

        Assert.Equal("Review the diff", sent.GetProperty("data").GetProperty("text").GetString());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Export_ReturnsADownload()
    {
        await using var fixture = await ServerFixture.StartAsync(new ScriptedModel(false, TextRound("exported text")));
        await fixture.WaitReadyAsync();
        using var events = await fixture.OpenEventsAsync(fixture.Http);
        await fixture.PostAsync(fixture.Http, "/input", new { text = "hi" });
        await events.WaitForAsync("turn.finished");

        var response = await fixture.Http.GetAsync("/export?format=markdown");

        response.EnsureSuccessStatusCode();
        Assert.Equal("text/markdown", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("attachment", response.Content.Headers.ContentDisposition?.ToString() ?? response.Headers.ToString(), StringComparison.Ordinal);
        Assert.Contains("exported text", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Images_AreRefusedForATextModelAndUnknownNumbers()
    {
        await using var fixture = await ServerFixture.StartAsync(new ScriptedModel(false));
        await fixture.WaitReadyAsync();

        var upload = await fixture.Http.PostAsync("/images", new ByteArrayContent([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0]));
        var missing = await fixture.Http.GetAsync("/images/7");

        Assert.Equal(HttpStatusCode.BadRequest, upload.StatusCode);
        Assert.Contains("do not support image input", await upload.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Image_UploadIsSentWithThePrompt()
    {
        var vision = new VisionModel();
        await using var fixture = await ServerFixture.StartAsync(
            new ScriptedModel(false) { Vision = vision },
            options: launch => launch with { Provider = "seen", Model = "eyes" },
            prepare: (home, _) => File.WriteAllText(
                Path.Combine(home.Root, "providers.json"),
                """
                {
                  "seen": {
                    "protocol": "ollama",
                    "baseUri": "http://127.0.0.1:9/",
                    "models": { "eyes": { "contextWindow": 8192, "imageInput": true } }
                  }
                }
                """));
        await fixture.WaitReadyAsync();
        using var events = await fixture.OpenEventsAsync(fixture.Http);
        byte[] png = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0];

        var attached = await ServerFixture.ReadJsonAsync(await fixture.Http.PostAsync("/images", new ByteArrayContent(png)));
        var number = attached.GetProperty("number").GetInt32();
        var bytes = await fixture.Http.GetByteArrayAsync("/images/" + number);
        await fixture.PostAsync(fixture.Http, "/input", new { text = "what is this " + attached.GetProperty("trustedMarker").GetString() });
        var sent = await events.WaitForAsync("message.sent");
        await events.WaitForAsync("turn.finished");

        Assert.Equal(png, bytes);
        Assert.Equal("what is this [Image #" + number + "]", sent.GetProperty("data").GetProperty("text").GetString());
        Assert.Equal(number, sent.GetProperty("data").GetProperty("images")[0].GetInt32());
        Assert.Contains(
            Assert.Single(vision.Requests).Items.OfType<Crystal.Multimodal.Chat.MultimodalMessage>().SelectMany(static message => message.Contents),
            content => content is Crystal.Multimodal.ImageContent);
    }

    [Fact]
    public async Task Resume_AsksTheRequestingClientToChooseASession()
    {
        await using var fixture = await ServerFixture.StartAsync(new ScriptedModel(false, TextRound("first"), TextRound("second")));
        await fixture.WaitReadyAsync();
        using var alice = fixture.Client("alice");
        using var bob = fixture.Client("bob");
        using var aliceEvents = await fixture.OpenEventsAsync(alice);
        using var bobEvents = await fixture.OpenEventsAsync(bob);
        await fixture.PostAsync(alice, "/input", new { text = "remember me" });
        await aliceEvents.WaitForAsync("turn.finished");
        await fixture.PostAsync(alice, "/input", new { text = "/clear" });
        await fixture.PostAsync(alice, "/input", new { text = "second session" });
        await aliceEvents.WaitForAsync("turn.finished", value => value.GetProperty("data").GetProperty("reason").GetString() == "completed"
            && aliceEvents.Types().Count(static type => type == "turn.finished") == 2);

        var resume = fixture.PostAsync(alice, "/input", new { text = "/resume" });
        var opened = await aliceEvents.WaitForAsync("interaction.opened", value =>
            value.GetProperty("data").GetProperty("interaction").GetProperty("kind").GetString() == "choose_session");
        var interaction = opened.GetProperty("data").GetProperty("interaction");
        Assert.Equal("alice", interaction.GetProperty("target").GetString());
        var choice = interaction.GetProperty("payload").GetProperty("sessions").EnumerateArray()
            .First(session => session.GetProperty("preview").GetString()!.Contains("remember", StringComparison.Ordinal))
            .GetProperty("id").GetString();
        var stolen = await bob.PostAsync("/interactions/" + interaction.GetProperty("id").GetString(), ServerFixture.Body(new { sessionId = choice }));
        await fixture.PostAsync(alice, "/interactions/" + interaction.GetProperty("id").GetString(), new { sessionId = choice });
        await resume;
        await aliceEvents.WaitForAsync("conversation.replayed");

        Assert.Equal(HttpStatusCode.Conflict, stolen.StatusCode);
        Assert.DoesNotContain(bobEvents.Types(), type => type == "interaction.opened");
        var snapshot = await fixture.GetAsync(bob, "/snapshot");
        Assert.Equal(choice, snapshot.GetProperty("sessionId").GetString());
        Assert.Contains(snapshot.GetProperty("conversation").EnumerateArray(), item => item.GetProperty("kind").GetString() == "message"
            && item.GetProperty("text").GetString() == "remember me");
    }

    [Fact]
    public async Task Children_ListIsEmptyWithoutAgents()
    {
        await using var fixture = await ServerFixture.StartAsync(new ScriptedModel(false));
        await fixture.WaitReadyAsync();

        var children = await fixture.GetAsync(fixture.Http, "/children");
        var missing = await fixture.Http.GetAsync("/children/nope");

        Assert.Empty(children.GetProperty("children").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Mode_CyclesAndSetsByName()
    {
        await using var fixture = await ServerFixture.StartAsync(new ScriptedModel(false));
        await fixture.WaitReadyAsync();

        var first = await fixture.PostAsync(fixture.Http, "/mode", new { action = "set", name = "plan" });
        var bad = await fixture.Http.PostAsync("/mode", ServerFixture.Body(new { action = "set", name = "missing" }));
        var cycled = await fixture.PostAsync(fixture.Http, "/mode", new { action = "cycle" });

        Assert.Equal("plan", first.GetProperty("mode").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal("work", cycled.GetProperty("mode").GetString());
    }

    [Fact]
    public async Task ClientRequestId_RunsAnInputOnce()
    {
        await using var fixture = await ServerFixture.StartAsync(new ScriptedModel(false, TextRound("once")));
        await fixture.WaitReadyAsync();
        using var events = await fixture.OpenEventsAsync(fixture.Http);

        await fixture.PostAsync(fixture.Http, "/input", new { text = "hi", clientRequestId = "r1" });
        await fixture.PostAsync(fixture.Http, "/input", new { text = "hi", clientRequestId = "r1" });
        await events.WaitForAsync("turn.finished");
        await fixture.PostAsync(fixture.Http, "/input", new { text = "/status" });
        await events.WaitForAsync("report.status");

        Assert.Single(events.Types(), type => type == "message.sent");
    }

    [Fact]
    public async Task Shutdown_ClosesTheSessionAndRemovesTheDiscoveryFile()
    {
        await using var fixture = await ServerFixture.StartAsync(new ScriptedModel(false, TextRound("saved")));
        await fixture.WaitReadyAsync();
        using var events = await fixture.OpenEventsAsync(fixture.Http);
        var listening = JsonDocument.Parse(fixture.Output.Trim()).RootElement;
        var discovery = listening.GetProperty("discovery").GetString()!;
        Assert.True(File.Exists(discovery));
        Assert.DoesNotContain(ServerFixture.Token, File.ReadAllText(discovery), StringComparison.Ordinal);
        Assert.DoesNotContain(ServerFixture.Token, fixture.Output, StringComparison.Ordinal);
        await fixture.PostAsync(fixture.Http, "/input", new { text = "hi" });
        await events.WaitForAsync("turn.finished");

        await fixture.PostAsync(fixture.Http, "/shutdown", new { });
        var closing = await events.WaitForAsync("server.closing");

        Assert.Equal(0, await fixture.ExitAsync());
        Assert.True(closing.GetProperty("data").GetProperty("saved").GetBoolean());
        Assert.Equal("request", closing.GetProperty("data").GetProperty("reason").GetString());
        var stopped = StoppedLine(fixture);
        Assert.Equal(0, stopped.GetProperty("exitCode").GetInt32());
        Assert.Equal("request", stopped.GetProperty("reason").GetString());
        Assert.True(stopped.GetProperty("saved").GetBoolean());
        Assert.Equal(closing.GetProperty("data").GetProperty("sessionId").GetString(), stopped.GetProperty("sessionId").GetString());
        await fixture.Server.DisposeAsync();
        Assert.False(File.Exists(discovery));
    }

    [Fact]
    public async Task Interrupt_AnnouncesClosingWithTheReason()
    {
        var model = new ScriptedModel(true);
        await using var fixture = await ServerFixture.StartAsync(model);
        await fixture.WaitReadyAsync();
        using var events = await fixture.OpenEventsAsync(fixture.Http);
        await fixture.PostAsync(fixture.Http, "/input", new { text = "long task" });
        await model.Held.Task.WaitAsync(ServerFixture.Wait);

        fixture.Server.Shutdown(Hosting.ServerStopReason.Interrupt);
        var state = await events.WaitForAsync("server.state", value =>
            value.GetProperty("data").GetProperty("state").GetString() == "closing");
        var closing = await events.WaitForAsync("server.closing");

        Assert.Equal(0, await fixture.ExitAsync());
        Assert.Equal("interrupt", state.GetProperty("data").GetProperty("reason").GetString());
        Assert.Equal("interrupt", closing.GetProperty("data").GetProperty("reason").GetString());
        Assert.Contains("turn.finished", events.Types());
        Assert.Contains("Stopping  Ctrl+C", fixture.Error, StringComparison.Ordinal);
        Assert.Contains("Session saved", fixture.Error, StringComparison.Ordinal);
        Assert.Contains("Server stopped", fixture.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Interrupt_WhileAwaitingTrustStopsWithoutFailing()
    {
        await using var fixture = await ServerFixture.StartAsync(new ScriptedModel(false), trusted: false);
        using var events = await fixture.OpenEventsAsync(fixture.Http);
        await events.WaitForAsync("interaction.opened");

        fixture.Server.Shutdown(Hosting.ServerStopReason.Interrupt);

        Assert.Equal(0, await fixture.ExitAsync());
        Assert.Equal("interrupt", StoppedLine(fixture).GetProperty("reason").GetString());
        Assert.False(StoppedLine(fixture).GetProperty("saved").GetBoolean());
        await events.WaitForAsync("interaction.cancelled");
        var state = await events.WaitForAsync("server.state", value =>
            value.GetProperty("data").GetProperty("state").GetString() == "closing");
        Assert.Equal("interrupt", state.GetProperty("data").GetProperty("reason").GetString());
        Assert.DoesNotContain("failed", events.Types().Where(static type => type == "server.state"));
        Assert.Contains("Waiting for a client to answer a startup question", fixture.Error, StringComparison.Ordinal);
        Assert.Contains("Server stopped", fixture.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Startup_SummaryListsTheAddressAfterTheListeningLine()
    {
        await using var fixture = await ServerFixture.StartAsync(new ScriptedModel(false));
        await fixture.WaitReadyAsync();
        var url = new Uri(JsonDocument.Parse(fixture.Output.Trim()).RootElement.GetProperty("url").GetString()!);

        var lines = fixture.Error.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains(lines, line => line == "Server     " + url.ToString().TrimEnd('/'));
        Assert.Contains(lines, line => line == "Listen     127.0.0.1");
        Assert.Contains(lines, line => line == "Port       " + url.Port);
        Assert.Contains(lines, line => line.StartsWith("Workspace  ", StringComparison.Ordinal));
        Assert.Contains(lines, line => line == "Auth       token");
        Assert.Contains(lines, line => line == "Token      supplied at launch");
        Assert.Contains("Ctrl+C stops the server.", lines);
        Assert.DoesNotContain(ServerFixture.Token, fixture.Error, StringComparison.Ordinal);
        Assert.Equal(1, fixture.Output.Trim().Split('\n').Length);
    }

    [Fact]
    public async Task QuitCommand_EndsTheProcess()
    {
        await using var fixture = await ServerFixture.StartAsync(new ScriptedModel(false));
        await fixture.WaitReadyAsync();

        using var events = await fixture.OpenEventsAsync(fixture.Http);
        var quit = await fixture.PostAsync(fixture.Http, "/input", new { text = "/quit" });
        var closing = await events.WaitForAsync("server.closing");

        Assert.True(quit.GetProperty("quit").GetBoolean());
        Assert.Equal(0, await fixture.ExitAsync());
        Assert.Equal("quit", closing.GetProperty("data").GetProperty("reason").GetString());
    }

    /// <summary>stdout is the listening line, then the stopped line.</summary>
    private static JsonElement StoppedLine(ServerFixture fixture)
    {
        var lines = fixture.Output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Equal("listening", JsonDocument.Parse(lines[0]).RootElement.GetProperty("event").GetString());
        var stopped = JsonDocument.Parse(lines[1]).RootElement.Clone();
        Assert.Equal("stopped", stopped.GetProperty("event").GetString());
        return stopped;
    }

    private static ChatStreamEvent[] TextRound(string text) =>
    [
        new ChatTextDelta(0, 0, ChatRole.Assistant, text),
        new ChatCandidateCompleted(0, FinishReason.Stop)
    ];

    private static ChatStreamEvent[] ToolRound(string callId, string name, string arguments) =>
    [
        new ChatToolCallDelta(0, 0, callId, name, arguments, ToolInputKind.Json),
        new ChatCandidateCompleted(0, FinishReason.ToolCalls)
    ];
}
