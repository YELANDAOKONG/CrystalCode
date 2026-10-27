using System.Text.Json;
using System.Text.Json.Nodes;

using CrystalCode.Bridge.Tests.Support;
using CrystalCode.Engine.Home;

using Xunit;

namespace CrystalCode.Bridge.Tests;

/// <summary>Forms, client MCP servers, and file image links.</summary>
public sealed class BridgeFormTests
{
    private static readonly object Forms = new { elicitation = new { form = new { } }, session = new { notices = new { } } };

    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    [Fact]
    public async Task Question_IsAFormAndTheAnswerReachesTheModel()
    {
        var question = """
            {"questions":[{"header":"Pick","question":"Which?","options":[{"label":"A","description":"a"},{"label":"B","description":"b"}],"custom":false}]}
            """;
        var model = new ScriptedModel(false, ScriptedModel.ToolRound("q-1", "question", question), ScriptedModel.TextRound("thanks"));
        await using var fixture = await BridgeFixture.StartAsync(model);
        fixture.OnRequest = static (method, _) => method == "elicitation/create"
            ? new JsonObject { ["action"] = "accept", ["content"] = new JsonObject { ["q1"] = "B" } }
            : null;
        var id = await fixture.OpenAsync(Forms);

        await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "ask me" } } });

        var form = (await fixture.WaitForAsync(static message => Method(message) == "elicitation/create")).GetProperty("params");
        Assert.Equal("form", form.GetProperty("mode").GetString());
        Assert.Equal(id, form.GetProperty("sessionId").GetString());
        Assert.Equal("q-1", form.GetProperty("toolCallId").GetString());
        var property = form.GetProperty("requestedSchema").GetProperty("properties").GetProperty("q1");
        Assert.Equal(["A", "B"], property.GetProperty("oneOf").EnumerateArray().Select(static option => option.GetProperty("const").GetString()));
        var result = fixture.Updates().Last(static update => update.TryGetProperty("toolCallId", out var call) && call.GetString() == "q-1");
        Assert.Contains("B", result.GetProperty("content")[0].GetProperty("content").GetProperty("text").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"action":"decline"}""")]
    [InlineData("""{"action":"cancel"}""")]
    [InlineData("""{"action":"accept","content":{"q1":"C"}}""")]
    public async Task Question_DeclinedOrInvalid_IsDismissed(string answer)
    {
        var question = """
            {"questions":[{"header":"Pick","question":"Which?","options":[{"label":"A","description":"a"}],"custom":false}]}
            """;
        var model = new ScriptedModel(false, ScriptedModel.ToolRound("q-1", "question", question), ScriptedModel.TextRound("ok"));
        await using var fixture = await BridgeFixture.StartAsync(model);
        fixture.OnRequest = (method, _) => method == "elicitation/create" ? JsonNode.Parse(answer) : null;
        var id = await fixture.OpenAsync(Forms);

        await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "ask me" } } });

        var result = fixture.Updates().Last(static update => update.TryGetProperty("toolCallId", out var call) && call.GetString() == "q-1");
        Assert.DoesNotContain("User answers", result.GetProperty("content")[0].GetProperty("content").GetProperty("text").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Question_WithoutFormSupport_IsDismissedWithoutAsking()
    {
        var question = """{"questions":[{"header":"Pick","question":"Which?","options":[],"custom":true}]}""";
        var model = new ScriptedModel(false, ScriptedModel.ToolRound("q-1", "question", question), ScriptedModel.TextRound("ok"));
        await using var fixture = await BridgeFixture.StartAsync(model);
        var id = await fixture.OpenAsync();

        await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "ask me" } } });

        lock (fixture.Lines)
        {
            Assert.DoesNotContain(fixture.Lines, static line => line.Contains("elicitation/create", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Trust_IsARequestScopedFormBeforeTheSessionOpens()
    {
        await using var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false), trusted: false);
        fixture.OnRequest = static (method, _) => method == "elicitation/create"
            ? new JsonObject { ["action"] = "accept", ["content"] = new JsonObject { ["trust"] = true } }
            : null;
        await fixture.RequestAsync("initialize", new { protocolVersion = 1, clientCapabilities = Forms });

        var (requestId, response) = await fixture.StartRequestAsync("session/new", new { cwd = fixture.Workspace, mcpServers = Array.Empty<object>() });
        var created = await response.WaitAsync(BridgeFixture.Wait);

        Assert.True(created.GetProperty("result").TryGetProperty("sessionId", out _));
        var form = (await fixture.WaitForAsync(static message => Method(message) == "elicitation/create")).GetProperty("params");
        Assert.Equal(requestId, form.GetProperty("requestId").GetInt64());
        Assert.False(form.TryGetProperty("sessionId", out _));
        Assert.Equal("boolean", form.GetProperty("requestedSchema").GetProperty("properties").GetProperty("trust").GetProperty("type").GetString());
        Assert.True(new WorkspaceTrustStore(new CrystalHome(fixture.Home)).Contains(fixture.Workspace));
    }

    [Fact]
    public async Task TrustDeclined_IsATrustRefusal()
    {
        await using var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false), trusted: false);
        fixture.OnRequest = static (method, _) => method == "elicitation/create" ? new JsonObject { ["action"] = "decline" } : null;
        await fixture.RequestAsync("initialize", new { protocolVersion = 1, clientCapabilities = Forms });

        var created = await fixture.RequestAsync("session/new", new { cwd = fixture.Workspace, mcpServers = Array.Empty<object>() });

        var error = created.GetProperty("error");
        Assert.Equal(-32603, error.GetProperty("code").GetInt32());
        Assert.Equal("trust_refused", error.GetProperty("data").GetProperty("crystal").GetProperty("kind").GetString());
        Assert.False(new WorkspaceTrustStore(new CrystalHome(fixture.Home)).Contains(fixture.Workspace));
    }

    [Fact]
    public async Task ResumeChoice_IsAFormWithTheSavedSessions()
    {
        var model = new ScriptedModel(false, ScriptedModel.TextRound("one"), ScriptedModel.TextRound("two"));
        await using var fixture = await BridgeFixture.StartAsync(model);
        string[] offered = [];
        fixture.OnRequest = (method, parameters) =>
        {
            if (method != "elicitation/create")
            {
                return null;
            }

            var choices = parameters.GetProperty("requestedSchema").GetProperty("properties").GetProperty("choice")
                .GetProperty("oneOf").EnumerateArray().ToArray();
            offered = [.. choices.Select(static choice => choice.GetProperty("const").GetString()!)];
            var first = choices.Single(static choice => choice.GetProperty("title").GetString()!.EndsWith("first session", StringComparison.Ordinal));
            return new JsonObject { ["action"] = "accept", ["content"] = new JsonObject { ["choice"] = first.GetProperty("const").GetString() } };
        };
        var first = await fixture.OpenAsync(Forms);
        await fixture.RequestAsync("session/prompt", new { sessionId = first, prompt = new[] { new { type = "text", text = "first session" } } });
        await fixture.RequestAsync("session/prompt", new { sessionId = first, prompt = new[] { new { type = "text", text = "/clear" } } });

        await fixture.RequestAsync("session/prompt", new { sessionId = first, prompt = new[] { new { type = "text", text = "/resume" } } });

        Assert.Contains(first, offered);
        await fixture.WaitForUpdateAsync("user_message_chunk", static update =>
            update.GetProperty("content").GetProperty("text").GetString() == "first session");
    }

    [Fact]
    public async Task McpServers_ReachTheSessionAndNeverShowTheirValues()
    {
        await using var fixture = await BridgeFixture.StartAsync(
            new ScriptedModel(false),
            options: static launch => launch with { Mcp = "on" });
        var missing = Path.Combine(fixture.Root, "no-such-server-binary");
        await fixture.RequestAsync("initialize", new { protocolVersion = 1, clientCapabilities = Forms });

        var created = await fixture.RequestAsync("session/new", new
        {
            cwd = fixture.Workspace,
            mcpServers = new object[]
            {
                new { name = "editor", command = missing, args = new[] { "--token", "sk-hidden-value" }, env = new[] { new { name = "TOKEN", value = "sk-env-value" } } },
                new { type = "http", name = "remote", url = "https://example.invalid/mcp", headers = new[] { new { name = "Authorization", value = "Bearer sk-header-value" } } }
            }
        });
        var id = created.GetProperty("result").GetProperty("sessionId").GetString()!;
        await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "/mcp" } } });

        var notices = fixture.Updates().Where(static update => update.GetProperty("sessionUpdate").GetString() == "notice")
            .Select(static update => update.GetProperty("title").GetString()!)
            .ToArray();
        Assert.Contains(notices, static text => text.Contains("editor  Session", StringComparison.Ordinal));
        Assert.Contains(notices, static text => text.Contains("remote  Session", StringComparison.Ordinal));
        lock (fixture.Lines)
        {
            foreach (var secret in new[] { "sk-hidden-value", "sk-env-value", "sk-header-value", missing, "example.invalid" })
            {
                Assert.All(fixture.Lines, line => Assert.DoesNotContain(secret, line, StringComparison.Ordinal));
            }
        }

        var saved = File.ReadAllText(Path.Combine(fixture.Home, "sessions", id + ".json"));
        Assert.DoesNotContain("sk-hidden-value", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("editor", saved, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FileImageLink_IsAttachedOnAVisionModelAndMentionedOtherwise()
    {
        var vision = new VisionModel();
        await using var fixture = await BridgeFixture.StartAsync(
            new ScriptedModel(false) { Vision = vision },
            options: static launch => launch with { Provider = "seen", Model = "eyes" },
            prepare: static (home, _) => File.WriteAllText(
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
        var image = Path.Combine(fixture.Workspace, "shot.png");
        File.WriteAllBytes(image, Png);
        var outside = Path.Combine(fixture.Root, "outside.png");
        File.WriteAllBytes(outside, Png);
        var id = await fixture.OpenAsync();

        await fixture.RequestAsync("session/prompt", new
        {
            sessionId = id,
            prompt = new object[]
            {
                new { type = "text", text = "compare" },
                new { type = "resource_link", uri = new Uri(image).AbsoluteUri, name = "shot.png", mimeType = "image/png" },
                new { type = "resource_link", uri = new Uri(outside).AbsoluteUri, name = "outside.png", mimeType = "image/png" }
            }
        });

        var request = Assert.Single(vision.Requests);
        var contents = request.Items.OfType<Crystal.Multimodal.Chat.MultimodalMessage>().SelectMany(static message => message.Contents).ToArray();
        Assert.Single(contents, static content => content is Crystal.Multimodal.ImageContent);
        var text = string.Concat(contents.OfType<Crystal.Multimodal.TextContent>().Select(static content => content.Text));
        Assert.Contains("[Linked file: outside.png at " + outside, text, StringComparison.Ordinal);
    }

    private static string? Method(JsonElement message) =>
        message.TryGetProperty("method", out var method) ? method.GetString() : null;
}
