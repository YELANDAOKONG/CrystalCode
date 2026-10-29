using System.Text.Json;
using System.Text.Json.Nodes;

using CrystalCode.Bridge.Tests.Support;
using CrystalCode.Engine.Home;

using Xunit;

namespace CrystalCode.Bridge.Tests;

/// <summary>The ACP flow over a pipe: initialize, sessions, prompts, cancel, and errors.</summary>
public sealed class BridgeProtocolTests
{
    [Fact]
    public async Task Initialize_AdvertisesWhatThisBuildImplements()
    {
        await using var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false));

        var response = await fixture.RequestAsync("initialize", new { protocolVersion = 7 });

        var result = response.GetProperty("result");
        Assert.Equal(1, result.GetProperty("protocolVersion").GetInt32());
        Assert.Equal("crystal-code", result.GetProperty("agentInfo").GetProperty("name").GetString());
        Assert.Equal("test", result.GetProperty("agentInfo").GetProperty("version").GetString());
        Assert.Equal(0, result.GetProperty("authMethods").GetArrayLength());
        var capabilities = result.GetProperty("agentCapabilities");
        Assert.True(capabilities.GetProperty("loadSession").GetBoolean());
        Assert.True(capabilities.GetProperty("promptCapabilities").GetProperty("image").GetBoolean());
        Assert.False(capabilities.GetProperty("promptCapabilities").GetProperty("audio").GetBoolean());
        Assert.True(capabilities.GetProperty("promptCapabilities").GetProperty("embeddedContext").GetBoolean());
        Assert.True(capabilities.GetProperty("mcpCapabilities").GetProperty("http").GetBoolean());
        Assert.True(capabilities.GetProperty("mcpCapabilities").GetProperty("sse").GetBoolean());
        var session = capabilities.GetProperty("sessionCapabilities");
        Assert.Equal(
            ["additionalDirectories", "close", "list", "resume"],
            session.EnumerateObject().Select(static property => property.Name).Order(StringComparer.Ordinal));
        Assert.False(session.TryGetProperty("fork", out _));
        var crystal = capabilities.GetProperty("_meta").GetProperty("crystal");
        Assert.True(crystal.GetProperty("loadAcrossCwd").GetBoolean());
        // Nothing outside the schema at the root of a spec type.
        Assert.Equal(
            ["_meta", "loadSession", "mcpCapabilities", "promptCapabilities", "sessionCapabilities"],
            capabilities.EnumerateObject().Select(static property => property.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task WorkspaceTrustOff_OpensAnUntrustedDirectoryWithoutAskingOrRecording()
    {
        await using var fixture = await BridgeFixture.StartAsync(
            new ScriptedModel(false),
            trusted: false,
            options: static launch => launch with { WorkspaceTrust = "off" });
        await fixture.RequestAsync("initialize", new { protocolVersion = 1 });

        var response = await fixture.RequestAsync("session/new", new { cwd = fixture.Workspace, mcpServers = Array.Empty<object>() });

        Assert.True(response.GetProperty("result").TryGetProperty("sessionId", out _));
        Assert.False(new WorkspaceTrustStore(new CrystalHome(fixture.Home)).Contains(fixture.Workspace));
    }

    [Fact]
    public async Task Prompt_StreamsTheReplyAndEndsTheTurn()
    {
        var model = new ScriptedModel(false, ScriptedModel.TextRound("hello there"));
        await using var fixture = await BridgeFixture.StartAsync(model);
        var id = await fixture.OpenAsync();

        var response = await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "hi" } } });

        var result = response.GetProperty("result");
        Assert.Equal("end_turn", result.GetProperty("stopReason").GetString());
        var crystal = result.GetProperty("_meta").GetProperty("crystal");
        Assert.Equal("completed", crystal.GetProperty("stopReason").GetString());
        Assert.Equal(1, crystal.GetProperty("modelCalls").GetInt32());
        var chunk = await fixture.WaitForUpdateAsync("agent_message_chunk");
        Assert.Equal("hello there", chunk.GetProperty("params").GetProperty("update").GetProperty("content").GetProperty("text").GetString());
        Assert.Equal("hi", model.LastUserText());
        Assert.DoesNotContain(fixture.Updates(), static update => update.GetProperty("sessionUpdate").GetString() == "user_message_chunk");
    }

    [Fact]
    public async Task Prompt_ReasoningBecomesThoughtChunks()
    {
        var model = new ScriptedModel(false,
        [
            new Crystal.Chat.ChatReasoningTextDelta(0, 0, 0, Crystal.Reasoning.ReasoningTextKind.Trace, "thinking it over"),
            new Crystal.Chat.ChatTextDelta(0, 1, Crystal.Chat.ChatRole.Assistant, "done"),
            new Crystal.Chat.ChatCandidateCompleted(0, Crystal.FinishReason.Stop)
        ]);
        await using var fixture = await BridgeFixture.StartAsync(model);
        var id = await fixture.OpenAsync();

        await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "think" } } });

        var thought = await fixture.WaitForUpdateAsync("agent_thought_chunk");
        Assert.Equal("thinking it over", thought.GetProperty("params").GetProperty("update").GetProperty("content").GetProperty("text").GetString());
    }

    [Fact]
    public async Task Prompt_SeparatesReasoningSummaryParts()
    {
        var model = new ScriptedModel(false,
        [
            new Crystal.Chat.ChatReasoningTextDelta(0, 0, 0, Crystal.Reasoning.ReasoningTextKind.Summary, "**One**"),
            new Crystal.Chat.ChatReasoningTextDelta(0, 0, 1, Crystal.Reasoning.ReasoningTextKind.Summary, "**Two**"),
            new Crystal.Chat.ChatTextDelta(0, 1, Crystal.Chat.ChatRole.Assistant, "done"),
            new Crystal.Chat.ChatCandidateCompleted(0, Crystal.FinishReason.Stop)
        ]);
        await using var fixture = await BridgeFixture.StartAsync(model);
        var id = await fixture.OpenAsync();

        await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "think" } } });

        var first = await fixture.WaitForUpdateAsync("agent_thought_chunk");
        var second = await fixture.WaitForUpdateAsync(
            "agent_thought_chunk",
            update => update.GetProperty("content").GetProperty("text").GetString() != "**One**");
        Assert.Equal("**One**", ThoughtText(first));
        Assert.Equal("\n\n**Two**", ThoughtText(second));
    }

    private static string? ThoughtText(JsonElement update) =>
        update.GetProperty("params").GetProperty("update").GetProperty("content").GetProperty("text").GetString();

    [Fact]
    public async Task Cancel_DuringStreaming_AnswersCancelled()
    {
        var model = new ScriptedModel(true);
        await using var fixture = await BridgeFixture.StartAsync(model);
        var id = await fixture.OpenAsync();
        var held = model.Held.Task;

        var (_, response) = await fixture.StartRequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "long" } } });
        await held.WaitAsync(BridgeFixture.Wait);
        await fixture.NotifyAsync("session/cancel", new { sessionId = id });

        var result = (await response.WaitAsync(BridgeFixture.Wait)).GetProperty("result");
        Assert.Equal("cancelled", result.GetProperty("stopReason").GetString());
    }

    [Fact]
    public async Task Cancel_AlsoStopsAFollowUpThatWasWaiting()
    {
        var model = new ScriptedModel(true);
        await using var fixture = await BridgeFixture.StartAsync(model);
        var id = await fixture.OpenAsync();
        var held = model.Held.Task;

        var (_, first) = await fixture.StartRequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "long" } } });
        await held.WaitAsync(BridgeFixture.Wait);
        var (_, second) = await fixture.StartRequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "later" } } });
        // The queue is not on the wire in this phase; give the follow-up time to queue.
        await Task.Delay(300);
        await fixture.NotifyAsync("session/cancel", new { sessionId = id });

        Assert.Equal("cancelled", (await first.WaitAsync(BridgeFixture.Wait)).GetProperty("result").GetProperty("stopReason").GetString());
        Assert.Equal("cancelled", (await second.WaitAsync(BridgeFixture.Wait)).GetProperty("result").GetProperty("stopReason").GetString());
    }

    [Fact]
    public async Task CancelRequest_ForAPrompt_InterruptsAndAnswersRequestCancelled()
    {
        var model = new ScriptedModel(true);
        await using var fixture = await BridgeFixture.StartAsync(model);
        var id = await fixture.OpenAsync();
        var held = model.Held.Task;

        var (requestId, response) = await fixture.StartRequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "long" } } });
        await held.WaitAsync(BridgeFixture.Wait);
        await fixture.NotifyAsync("$/cancel_request", new { requestId });

        var error = (await response.WaitAsync(BridgeFixture.Wait)).GetProperty("error");
        Assert.Equal(-32800, error.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task SlashCommand_EndsTheTurnAfterItsOutput()
    {
        await using var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false));
        var id = await fixture.OpenAsync(new { session = new { notices = new { } } });

        var response = await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "/plan" } } });

        Assert.Equal("end_turn", response.GetProperty("result").GetProperty("stopReason").GetString());
        Assert.False(response.GetProperty("result").TryGetProperty("_meta", out _));
    }

    [Fact]
    public async Task Quit_ClosesTheSession()
    {
        await using var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false));
        var id = await fixture.OpenAsync();

        var quit = await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "/quit" } } });
        Assert.Equal("end_turn", quit.GetProperty("result").GetProperty("stopReason").GetString());

        var after = await WaitForInactiveAsync(fixture, id);
        Assert.Equal(-32002, after.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Close_EndsTheSessionAndAnotherCanOpen()
    {
        await using var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false, ScriptedModel.TextRound("one")));
        var id = await fixture.OpenAsync();
        await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "hi" } } });

        var closed = await fixture.RequestAsync("session/close", new { sessionId = id });
        var after = await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "hi" } } });
        var reopened = await fixture.RequestAsync("session/new", new { cwd = fixture.Workspace, mcpServers = Array.Empty<object>() });

        Assert.True(closed.TryGetProperty("result", out _));
        Assert.Equal(-32002, after.GetProperty("error").GetProperty("code").GetInt32());
        Assert.NotEqual(id, reopened.GetProperty("result").GetProperty("sessionId").GetString());
        Assert.Contains("saved", fixture.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clear_KeepsTheAcpSessionId()
    {
        await using var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false));
        var id = await fixture.OpenAsync();

        await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "/clear" } } });
        var after = await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "still here" } } });

        Assert.Equal("end_turn", after.GetProperty("result").GetProperty("stopReason").GetString());
    }

    [Fact]
    public async Task Errors_UseOnlyTheDocumentedCodes()
    {
        await using var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false));
        var id = await fixture.OpenAsync();

        await fixture.WriteLineAsync("{not json");
        await fixture.WriteLineAsync("""[{"jsonrpc":"2.0","id":99,"method":"initialize"}]""");
        var unknown = await fixture.RequestAsync("session/delete", new { sessionId = id });
        var other = await fixture.RequestAsync("session/prompt", new { sessionId = "nope", prompt = new[] { new { type = "text", text = "hi" } } });
        var audio = await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new object[] { new { type = "audio", data = "AA==", mimeType = "audio/wav" } } });
        var empty = await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = Array.Empty<object>() });
        // A failed open replaces nothing, but a later valid one would; these come last.
        var relative = await fixture.RequestAsync("session/new", new { cwd = "relative/path", mcpServers = Array.Empty<object>() });
        var roots = await fixture.RequestAsync("session/new", new { cwd = fixture.Workspace, additionalDirectories = new[] { "relative" } });
        var missing = await fixture.RequestAsync("session/load", new { sessionId = "no-such-session", cwd = fixture.Workspace, mcpServers = Array.Empty<object>() });

        Assert.Equal(-32601, Code(unknown));
        Assert.Equal(-32602, Code(relative));
        Assert.Equal(-32602, Code(roots));
        Assert.Equal(-32002, Code(missing));
        Assert.Equal(-32002, Code(other));
        Assert.Equal(-32602, Code(audio));
        Assert.Equal(-32602, Code(empty));
        await fixture.WaitForAsync(static _ => true);
        lock (fixture.Lines)
        {
            Assert.Contains(fixture.Lines, static line => line.Contains("-32700", StringComparison.Ordinal));
            Assert.Contains(fixture.Lines, static line => line.Contains("-32600", StringComparison.Ordinal));
            Assert.DoesNotContain(fixture.Lines, static line => line.Contains("-32000", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task MissingCredential_IsALaunchRefusal()
    {
        await using var fixture = await BridgeFixture.StartAsync(
            new ScriptedModel(false),
            options: static launch => launch with { Provider = "openai", Model = "gpt-5.2" });
        await fixture.RequestAsync("initialize", new { protocolVersion = 1 });

        var response = await fixture.RequestAsync("session/new", new { cwd = fixture.Workspace, mcpServers = Array.Empty<object>() });

        var error = response.GetProperty("error");
        Assert.Equal(-32603, error.GetProperty("code").GetInt32());
        Assert.Equal("launch_refused", error.GetProperty("data").GetProperty("crystal").GetProperty("kind").GetString());
    }

    [Fact]
    public async Task UntrustedWorkspace_IsATrustRefusalNamingTheDirectory()
    {
        await using var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false), trusted: false);
        await fixture.RequestAsync("initialize", new { protocolVersion = 1 });

        var response = await fixture.RequestAsync("session/new", new { cwd = fixture.Workspace, mcpServers = Array.Empty<object>() });

        var error = response.GetProperty("error");
        Assert.Equal(-32603, error.GetProperty("code").GetInt32());
        Assert.Equal("trust_refused", error.GetProperty("data").GetProperty("crystal").GetProperty("kind").GetString());
        Assert.Contains(Path.GetFullPath(fixture.Workspace), error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.False(new WorkspaceTrustStore(new CrystalHome(fixture.Home)).Contains(fixture.Workspace));
    }

    [Fact]
    public async Task McpServersFromTheClient_AreRejectedWithoutEchoingTheirValues()
    {
        await using var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false));
        await fixture.RequestAsync("initialize", new { protocolVersion = 1 });

        var relative = await fixture.RequestAsync("session/new", new
        {
            cwd = fixture.Workspace,
            mcpServers = new[] { new { name = "editor", command = "./secret-command", args = new[] { "--token", "sk-hidden-value" }, env = Array.Empty<object>() } }
        });

        Assert.Equal(-32602, Code(relative));
        var message = relative.GetProperty("error").GetRawText();
        Assert.Contains("editor", message, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-hidden-value", message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-command", message, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-hidden-value", fixture.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClosingStdin_ShutsDownAndAnswersTheRunningPrompt()
    {
        var model = new ScriptedModel(true);
        await using var fixture = await BridgeFixture.StartAsync(model);
        var id = await fixture.OpenAsync();
        var held = model.Held.Task;

        var (_, response) = await fixture.StartRequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "long" } } });
        await held.WaitAsync(BridgeFixture.Wait);
        await fixture.CloseInputAsync();

        await fixture.Run.WaitAsync(BridgeFixture.Wait);
        var answer = await response.WaitAsync(BridgeFixture.Wait);
        Assert.Equal(-32800, Code(answer));
    }

    [Fact]
    public async Task StdoutCarriesOnlyProtocolLines()
    {
        await using var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false, ScriptedModel.TextRound("ok")));
        var id = await fixture.OpenAsync();

        await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "/help" } } });
        await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "hi" } } });

        lock (fixture.Lines)
        {
            Assert.All(fixture.Lines, static line =>
            {
                using var document = JsonDocument.Parse(line);
                Assert.Equal("2.0", document.RootElement.GetProperty("jsonrpc").GetString());
            });
        }
    }

    private static int Code(JsonElement response) =>
        response.GetProperty("error").GetProperty("code").GetInt32();

    private static async Task<JsonElement> WaitForInactiveAsync(BridgeFixture fixture, string id)
    {
        var deadline = DateTime.UtcNow + BridgeFixture.Wait;
        while (true)
        {
            var response = await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "/status" } } });
            if (response.TryGetProperty("error", out _) || DateTime.UtcNow > deadline)
            {
                return response;
            }

            await Task.Delay(50);
        }
    }

    internal static JsonNode Selected(string option) =>
        new JsonObject { ["outcome"] = new JsonObject { ["outcome"] = "selected", ["optionId"] = option } };
}
