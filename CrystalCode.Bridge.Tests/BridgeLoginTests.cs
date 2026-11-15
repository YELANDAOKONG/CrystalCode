using System.Text.Json;
using System.Text.Json.Nodes;

using CrystalCode.Bridge.Tests.Support;
using CrystalCode.Bridge.Transport;

using Xunit;

namespace CrystalCode.Bridge.Tests;

public sealed class BridgeLoginTests
{
    private const string Secret = "code-SECRET-1234567890";
    private static readonly string Address = "http://127.0.0.1:1455/auth/callback?code=" + Secret + "&state=abc";
    private static readonly object Forms = new { elicitation = new { form = new { } }, session = new { notices = new { } } };

    private static Task<BridgeFixture> StartAsync(TextWriter? debug = null) =>
        BridgeFixture.StartAsync(
            new ScriptedModel(false),
            prepare: (home, _) => LoginFakeIntegration.WriteProviders(home.Root),
            debug: debug,
            configurePlugins: registry => registry.Add(new LoginFakeIntegration()));

    private static async Task<(long Id, Task<JsonElement> Response)> LoginAsync(BridgeFixture fixture, string id, string command = "/login account") =>
        await fixture.StartRequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = command } } });

    private static async Task WaitForLinkAsync(BridgeFixture fixture, int times)
    {
        var deadline = DateTime.UtcNow + BridgeFixture.Wait;
        while (DateTime.UtcNow < deadline)
        {
            if (fixture.Updates().Count(static update => update.TryGetProperty("content", out var content)
                && content.TryGetProperty("text", out var text)
                && (text.GetString() ?? string.Empty).Contains("https://example.test/authorize", StringComparison.Ordinal)) >= times)
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("The sign-in link was not shown " + times + " times.");
    }

    private static bool Elicitation(JsonElement message) =>
        message.TryGetProperty("method", out var method) && method.GetString() == "elicitation/create";

    [Fact]
    public async Task Login_IsAFormWithTheLinkAndTheAddressSignsIn()
    {
        await using var fixture = await StartAsync();
        fixture.OnRequest = static (method, _) => method == "elicitation/create"
            ? new JsonObject { ["action"] = "accept", ["content"] = new JsonObject { ["loginAddress"] = Address } }
            : null;
        var id = await fixture.OpenAsync(Forms);

        var (_, response) = await LoginAsync(fixture, id);
        await response.WaitAsync(BridgeFixture.Wait);

        var form = (await fixture.WaitForAsync(Elicitation)).GetProperty("params");
        var message = form.GetProperty("message").GetString()!;
        Assert.Equal("form", form.GetProperty("mode").GetString());
        Assert.Equal(id, form.GetProperty("sessionId").GetString());
        Assert.Contains("Sign in to account.", message, StringComparison.Ordinal);
        Assert.Contains("https://example.test/authorize?state=abc", message, StringComparison.Ordinal);
        Assert.Equal(
            "string",
            form.GetProperty("requestedSchema").GetProperty("properties").GetProperty("loginAddress").GetProperty("type").GetString());
        var models = (await fixture.RequestAsync("_crystal/models", new { sessionId = id })).GetProperty("result");
        Assert.True(models.GetProperty("models").EnumerateArray()
            .Single(static model => model.GetProperty("provider").GetString() == "account").GetProperty("credentialReady").GetBoolean());
    }

    [Theory]
    [InlineData("""{"action":"decline"}""")]
    [InlineData("""{"action":"cancel"}""")]
    [InlineData("""{"action":"accept","content":{"loginAddress":"code=abc"}}""")]
    public async Task Login_DeclinedOrInvalidEndsTheSignInWithoutSavingAnything(string answer)
    {
        await using var fixture = await StartAsync();
        fixture.OnRequest = (method, _) => method == "elicitation/create" ? JsonNode.Parse(answer) : null;
        var id = await fixture.OpenAsync(Forms);

        var (_, response) = await LoginAsync(fixture, id);
        await response.WaitAsync(BridgeFixture.Wait);

        var models = (await fixture.RequestAsync("_crystal/models", new { sessionId = id })).GetProperty("result");
        Assert.False(models.GetProperty("models").EnumerateArray()
            .Single(static model => model.GetProperty("provider").GetString() == "account").GetProperty("credentialReady").GetBoolean());
    }

    [Fact]
    public async Task Login_WithoutFormsSendsTheLinkAsTextAndCancelFreesTheNextOne()
    {
        await using var fixture = await StartAsync();
        var id = await fixture.OpenAsync();

        var (_, first) = await LoginAsync(fixture, id);
        await fixture.WaitForUpdateAsync(
            "agent_message_chunk",
            static update => update.GetProperty("content").GetProperty("text").GetString()!.Contains("https://example.test/authorize", StringComparison.Ordinal));
        Assert.DoesNotContain(fixture.Lines, static line => line.Contains("elicitation/create", StringComparison.Ordinal));

        await fixture.NotifyAsync("session/cancel", new { sessionId = id });
        await first.WaitAsync(BridgeFixture.Wait);
        Assert.Contains(
            fixture.Updates(),
            static update => update.TryGetProperty("content", out var content)
                && content.TryGetProperty("text", out var text)
                && text.GetString() == "Signing in was cancelled.");

        var (_, second) = await LoginAsync(fixture, id);
        await WaitForLinkAsync(fixture, 2);
        await fixture.NotifyAsync("session/cancel", new { sessionId = id });
        await second.WaitAsync(BridgeFixture.Wait);
    }

    [Fact]
    public async Task TheAddressNeverReachesTheDebugLog()
    {
        var debug = new StringWriter();
        await using var fixture = await StartAsync(debug);
        fixture.OnRequest = static (method, _) => method == "elicitation/create"
            ? new JsonObject { ["action"] = "accept", ["content"] = new JsonObject { ["loginAddress"] = Address } }
            : null;
        var id = await fixture.OpenAsync(Forms);

        var (_, response) = await LoginAsync(fixture, id);
        await response.WaitAsync(BridgeFixture.Wait);

        Assert.DoesNotContain(Secret, debug.ToString(), StringComparison.Ordinal);
        Assert.Contains(ProtocolLog.Redacted, debug.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, string.Join('\n', fixture.Lines), StringComparison.Ordinal);
    }
}
