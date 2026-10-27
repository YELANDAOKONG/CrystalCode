using System.Text.Json;

using CrystalCode.Bridge.Tests.Support;

using Xunit;

namespace CrystalCode.Bridge.Tests;

/// <summary>Tool call updates and permission requests over the wire.</summary>
public sealed class BridgeToolTests
{
    [Fact]
    public async Task Permission_AllowOnce_RunsTheToolAndCompletesTheCall()
    {
        var model = new ScriptedModel(false,
            ScriptedModel.ToolRound("call-1", "bash", """{"command":"echo bridged"}"""),
            ScriptedModel.TextRound("done"));
        await using var fixture = await BridgeFixture.StartAsync(model);
        fixture.OnRequest = static (method, _) => method == "session/request_permission" ? BridgeProtocolTests.Selected("once") : null;
        var id = await fixture.OpenAsync();

        var response = await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "run it" } } });

        Assert.Equal("end_turn", response.GetProperty("result").GetProperty("stopReason").GetString());
        var request = await fixture.WaitForAsync(static message => Method(message) == "session/request_permission");
        var parameters = request.GetProperty("params");
        Assert.Equal(id, parameters.GetProperty("sessionId").GetString());
        var call = parameters.GetProperty("toolCall");
        Assert.Equal("call-1", call.GetProperty("toolCallId").GetString());
        Assert.Equal("execute", call.GetProperty("kind").GetString());
        Assert.Equal("Bash  echo bridged", call.GetProperty("title").GetString());
        Assert.Equal("echo bridged", call.GetProperty("rawInput").GetProperty("command").GetString());
        var options = parameters.GetProperty("options").EnumerateArray()
            .Select(static option => option.GetProperty("optionId").GetString())
            .ToArray();
        Assert.Equal("once", options[0]);
        Assert.Equal("deny", options[^1]);

        var updates = fixture.Updates().Where(static update => ToolCallId(update) == "call-1").ToArray();
        Assert.Equal("tool_call", updates[0].GetProperty("sessionUpdate").GetString());
        Assert.Equal(1, updates.Count(static update => update.GetProperty("sessionUpdate").GetString() == "tool_call"));
        var final = updates[^1];
        Assert.Equal("completed", final.GetProperty("status").GetString());
        Assert.Contains("bridged", final.GetProperty("content")[0].GetProperty("content").GetProperty("text").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Permission_Deny_FailsTheCallWithTheRejection()
    {
        var model = new ScriptedModel(false,
            ScriptedModel.ToolRound("call-1", "bash", """{"command":"echo never"}"""),
            ScriptedModel.TextRound("ok"));
        await using var fixture = await BridgeFixture.StartAsync(model);
        fixture.OnRequest = static (method, _) => method == "session/request_permission" ? BridgeProtocolTests.Selected("deny") : null;
        var id = await fixture.OpenAsync();

        await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "run it" } } });

        var final = fixture.Updates().Last(static update => ToolCallId(update) == "call-1");
        Assert.Equal("failed", final.GetProperty("status").GetString());
        Assert.DoesNotContain("never\n", final.GetProperty("content")[0].GetProperty("content").GetProperty("text").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Permission_UnofferedOrCancelledOutcome_IsDeny()
    {
        var model = new ScriptedModel(false,
            ScriptedModel.ToolRound("call-1", "bash", """{"command":"echo one"}"""),
            ScriptedModel.ToolRound("call-2", "bash", """{"command":"echo two"}"""),
            ScriptedModel.TextRound("ok"));
        await using var fixture = await BridgeFixture.StartAsync(model);
        var answers = new Queue<JsonElement>(
        [
            JsonDocument.Parse("""{"outcome":{"outcome":"selected","optionId":"maybe"}}""").RootElement,
            JsonDocument.Parse("""{"outcome":{"outcome":"cancelled"}}""").RootElement
        ]);
        fixture.OnRequest = (method, _) => method == "session/request_permission"
            ? System.Text.Json.Nodes.JsonNode.Parse(answers.Dequeue().GetRawText())
            : null;
        var id = await fixture.OpenAsync();

        await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "run" } } });

        Assert.Equal("failed", fixture.Updates().Last(static update => ToolCallId(update) == "call-1").GetProperty("status").GetString());
        Assert.Equal("failed", fixture.Updates().Last(static update => ToolCallId(update) == "call-2").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Cancel_DuringAPendingPermission_CancelsTheRequestAndTheTurn()
    {
        var model = new ScriptedModel(false, ScriptedModel.ToolRound("call-1", "bash", """{"command":"echo wait"}"""));
        await using var fixture = await BridgeFixture.StartAsync(model);
        var id = await fixture.OpenAsync();

        var (_, response) = await fixture.StartRequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "run" } } });
        var request = await fixture.WaitForAsync(static message => Method(message) == "session/request_permission");
        await fixture.NotifyAsync("session/cancel", new { sessionId = id });

        Assert.Equal("cancelled", (await response.WaitAsync(BridgeFixture.Wait)).GetProperty("result").GetProperty("stopReason").GetString());
        var cancelled = await fixture.WaitForAsync(static message => Method(message) == "$/cancel_request");
        Assert.Equal(request.GetProperty("id").GetInt64(), cancelled.GetProperty("params").GetProperty("requestId").GetInt64());
    }

    [Fact]
    public async Task Edit_SendsADiffComputedFromDisk()
    {
        var model = new ScriptedModel(false,
            ScriptedModel.ToolRound("call-1", "edit", """{"path":"notes.txt","old_string":"alpha","new_string":"beta"}"""),
            ScriptedModel.TextRound("edited"));
        await using var fixture = await BridgeFixture.StartAsync(model, options: static launch => launch with { Approval = "edit" });
        File.WriteAllText(Path.Combine(fixture.Workspace, "notes.txt"), "alpha\n");
        var id = await fixture.OpenAsync();

        await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "edit" } } });

        var first = fixture.Updates().First(static update => ToolCallId(update) == "call-1");
        Assert.Equal("tool_call", first.GetProperty("sessionUpdate").GetString());
        Assert.Equal("edit", first.GetProperty("kind").GetString());
        var announced = fixture.Updates().First(static update => ToolCallId(update) == "call-1"
            && update.GetProperty("_meta").GetProperty("crystal").GetProperty("event").GetString() == "ToolCallsIssued");
        Assert.Equal("Edit  notes.txt", announced.GetProperty("title").GetString());
        var path = Path.Combine(fixture.Workspace, "notes.txt");
        // The engine resolves the workspace root, so /var may read /private/var.
        var location = announced.GetProperty("locations")[0].GetProperty("path").GetString()!;
        Assert.True(Path.IsPathFullyQualified(location));
        Assert.EndsWith(Path.Combine("workspace", "notes.txt"), location, StringComparison.Ordinal);
        var diff = announced.GetProperty("content")[0];
        Assert.Equal("diff", diff.GetProperty("type").GetString());
        Assert.Equal("alpha\n", diff.GetProperty("oldText").GetString());
        Assert.Equal("beta\n", diff.GetProperty("newText").GetString());
        var passed = fixture.Updates().First(static update => ToolCallId(update) == "call-1"
            && update.GetProperty("_meta").GetProperty("crystal").GetProperty("event").GetString() == "ApprovalPassed");
        Assert.Equal("in_progress", passed.GetProperty("status").GetString());
        Assert.Equal("beta\n", File.ReadAllText(path));
    }

    [Fact]
    public async Task Todos_BecomeAPlan()
    {
        var model = new ScriptedModel(false,
            ScriptedModel.ToolRound("call-1", "todowrite", """{"todos":[{"id":"a","content":"First","status":"in_progress"},{"id":"b","content":"Second","status":"cancelled"}]}"""),
            ScriptedModel.TextRound("planned"));
        await using var fixture = await BridgeFixture.StartAsync(model);
        var id = await fixture.OpenAsync();

        await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "plan" } } });

        var plan = fixture.Updates().Last(static update => update.GetProperty("sessionUpdate").GetString() == "plan");
        var entries = plan.GetProperty("entries");
        Assert.Equal(2, entries.GetArrayLength());
        Assert.Equal("in_progress", entries[0].GetProperty("status").GetString());
        Assert.Equal("completed", entries[1].GetProperty("status").GetString());
        Assert.Equal("cancelled", entries[1].GetProperty("_meta").GetProperty("crystal").GetProperty("status").GetString());
    }

    private static string? Method(JsonElement message) =>
        message.TryGetProperty("method", out var method) ? method.GetString() : null;

    private static string? ToolCallId(JsonElement update) =>
        update.TryGetProperty("toolCallId", out var id) ? id.GetString() : null;
}
