using System.Net;
using System.Text.Json;

using Crystal;
using Crystal.Chat;

using CrystalCode.Server.Tests.Support;

using Xunit;

namespace CrystalCode.Server.Tests;

public sealed class ServerPursueTests
{
    [Fact]
    public async Task Configuration_ExposesChoicesAndLimitsAndPersistsValidatedEdits()
    {
        await using var fixture = await ServerFixture.StartAsync(new ScriptedModel(false),
            prepare: static (home, _) => File.WriteAllText(Path.Combine(home.Root, "providers.json"),
                """{"side":{"protocol":"ollama","baseUri":"http://127.0.0.1:9/","models":{"tuned":{"contextWindow":8192,"serviceTiers":["priority"]}}}}"""));
        await fixture.WaitReadyAsync();
        var initial = (await fixture.GetAsync(fixture.Http, "/pursue/configuration")).GetProperty("configuration");
        Assert.True(initial.GetProperty("model").GetProperty("usesSessionModel").GetBoolean());
        Assert.Equal(604800, initial.GetProperty("limits").GetProperty("durationSeconds").GetInt32());
        await PutAsync("/options/pursue_model", new { value = "side/tuned" });
        await PutAsync("/options/pursue_tier", new { value = "priority" });
        await PutAsync("/options/pursue_compaction_model", new { value = "side/tuned" });
        var updated = (await PutAsync("/pursue/limits/continuations", new { value = 42 })).GetProperty("configuration");
        Assert.Equal(42, updated.GetProperty("limits").GetProperty("continuations").GetInt32());
        Assert.Equal("priority", updated.GetProperty("model").GetProperty("tier").GetString());
        Assert.Equal("side", updated.GetProperty("compaction").GetProperty("provider").GetString());
        var unlimited = (await PutAsync("/pursue/limits/wait", new { value = (int?)null })).GetProperty("configuration");
        Assert.Equal(JsonValueKind.Null, unlimited.GetProperty("limits").GetProperty("waitSeconds").ValueKind);

        foreach (var (name, value) in new (string, object)[]
                 { ("stalls", new { value = (int?)null }), ("duration", new { value = -1 }),
                     ("unknown", new { value = 1 }), ("wait", new { }), ("wait", new { value = "unlimited" }) })
        {
            using var invalid = await fixture.Http.PutAsync("/pursue/limits/" + name, ServerFixture.Body(value));
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }

        var issued = await fixture.PostAsync(fixture.Http, "/auth/tokens", new { scope = "observe" });
        using var observer = fixture.Client("observer", issued.GetProperty("token").GetString());
        await fixture.GetAsync(observer, "/pursue/configuration");
        using var forbidden = await observer.PutAsync("/pursue/limits/continuations", ServerFixture.Body(new { value = 5 }));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var stored = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixture.Home, "config.json")));
        Assert.Equal(42, stored.RootElement.GetProperty("pursueLimits").GetProperty("maximumContinuations").GetInt32());
        Assert.Equal("tuned", stored.RootElement.GetProperty("pursueModel").GetProperty("model").GetString());

        async Task<JsonElement> PutAsync(string path, object body) =>
            await ServerFixture.ReadJsonAsync(await fixture.Http.PutAsync(path, ServerFixture.Body(body)));
    }

    [Fact]
    public async Task Configuration_RefusesChangesWhileWorking()
    {
        var model = new ScriptedModel(true);
        await using var fixture = await ServerFixture.StartAsync(model);
        await fixture.WaitReadyAsync();
        await fixture.PostAsync(fixture.Http, "/input", new { text = "/pursue start work" });
        await model.Held.Task.WaitAsync(ServerFixture.Wait);
        using var refused = await fixture.Http.PutAsync("/pursue/limits/stalls", ServerFixture.Body(new { value = 2 }));
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var configuration = (await fixture.GetAsync(fixture.Http, "/pursue/configuration")).GetProperty("configuration");
        Assert.Equal(10, configuration.GetProperty("limits").GetProperty("stalls").GetInt32());
    }

    [Fact]
    public async Task Checks_AreReadableWithoutReplayingEventsAndIncludeEvidence()
    {
        var model = new ScriptedModel(false, Text("Tests passed."),
            Text("""{"verdict":"done","reason":"Tests passed.","evidence":[2]}"""));
        await using var fixture = await ServerFixture.StartAsync(model);
        await fixture.WaitReadyAsync();
        using var events = await fixture.OpenEventsAsync(fixture.Http);
        await fixture.PostAsync(fixture.Http, "/input", new { text = "/pursue start fix tests" });
        var checkedEvent = await events.WaitForAsync("pursue.checked");
        await events.WaitForAsync("turn.finished");

        var snapshot = await fixture.GetAsync(fixture.Http, "/snapshot");
        var latest = snapshot.GetProperty("pursue").GetProperty("latestCheck");
        var id = latest.GetProperty("id").GetString();
        Assert.Equal(checkedEvent.GetProperty("data").GetProperty("id").GetString(), id);
        Assert.Contains("Tests passed.", latest.GetProperty("evidenceContent")[0].GetProperty("text").GetString());
        var page = await fixture.GetAsync(fixture.Http, "/pursue/checks?limit=1");
        Assert.Equal(id, page.GetProperty("checks")[0].GetProperty("id").GetString());
        Assert.False(page.GetProperty("hasMore").GetBoolean());
        var detail = await fixture.GetAsync(fixture.Http, "/pursue/checks/" + id);
        Assert.Equal(latest.GetRawText(), detail.GetProperty("check").GetRawText());
        var current = await fixture.GetAsync(fixture.Http, "/pursue");
        Assert.Equal(1, current.GetProperty("pursue").GetProperty("checkCount").GetInt32());
        using var invalid = await fixture.Http.GetAsync("/pursue/checks?limit=101");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var missing = await fixture.Http.GetAsync("/pursue/checks/missing");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var issued = await fixture.PostAsync(fixture.Http, "/auth/tokens", new { scope = "observe" });
        using var observer = fixture.Client("observer", issued.GetProperty("token").GetString());
        Assert.Equal(id, (await fixture.GetAsync(observer, "/pursue/checks/" + id)).GetProperty("check").GetProperty("id").GetString());
    }

    private static IReadOnlyList<ChatStreamEvent> Text(string text) =>
    [
        new ChatTextDelta(0, 0, ChatRole.Assistant, text),
        new ChatCandidateCompleted(0, FinishReason.Stop)
    ];
}
