using System.Net;

using Crystal;
using Crystal.Chat;

using CrystalCode.Server.Tests.Support;

using Xunit;

namespace CrystalCode.Server.Tests;

public sealed class ServerPursueTests
{
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
