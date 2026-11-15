using System.Net;
using System.Text.Json;

using CrystalCode.Server.Tests.Support;

using Xunit;

namespace CrystalCode.Server.Tests;

public sealed class ServerLoginTests
{
    private const string Secret = "code-SECRET-1234567890";
    private static readonly string Address = "http://127.0.0.1:1455/auth/callback?code=" + Secret + "&state=abc";

    private static Task<ServerFixture> StartAsync() =>
        ServerFixture.StartAsync(
            new ScriptedModel(false),
            prepare: (home, _) => LoginFakeIntegration.WriteProviders(home.Root),
            configurePlugins: registry => registry.Add(new LoginFakeIntegration()));

    private static JsonElement Interaction(JsonElement opened) =>
        opened.GetProperty("data").GetProperty("interaction");

    private static bool IsLogin(JsonElement value) =>
        Interaction(value).GetProperty("kind").GetString() == "login";

    [Fact]
    public async Task Login_OpensAnInteractionWithTheLinkAndAnAddressAnswerSignsIn()
    {
        await using var fixture = await StartAsync();
        await fixture.WaitReadyAsync();
        using var events = await fixture.OpenEventsAsync(fixture.Http);

        var login = fixture.PostAsync(fixture.Http, "/input", new { text = "/login account" });
        var interaction = Interaction(await events.WaitForAsync("interaction.opened", IsLogin));

        var payload = interaction.GetProperty("payload");
        Assert.Equal("test-client", interaction.GetProperty("target").GetString());
        Assert.Equal("account", payload.GetProperty("provider").GetString());
        Assert.Equal("https://example.test/authorize?state=abc", payload.GetProperty("link").GetString());
        Assert.Equal("Open this address:", payload.GetProperty("linkLabel").GetString());
        Assert.Equal("Paste the address it ended on:", payload.GetProperty("prompt").GetString());
        Assert.Equal("Continue in your browser.", payload.GetProperty("steps")[0].GetString());

        var models = await fixture.GetAsync(fixture.Http, "/models");
        var before = models.GetProperty("models").EnumerateArray().Single(static model => model.GetProperty("provider").GetString() == "account");
        Assert.Equal("authentication", before.GetProperty("type").GetString());
        Assert.False(before.GetProperty("credentialReady").GetBoolean());

        await fixture.PostAsync(fixture.Http, "/interactions/" + interaction.GetProperty("id").GetString(), new { address = Address });
        await login;

        var after = (await fixture.GetAsync(fixture.Http, "/models")).GetProperty("models").EnumerateArray()
            .Single(static model => model.GetProperty("provider").GetString() == "account");
        Assert.True(after.GetProperty("credentialReady").GetBoolean());
        await events.WaitForAsync("interaction.resolved");
        await events.WaitForAsync("note", static value =>
            value.GetProperty("data").GetProperty("text").GetString() == "Signed in to account.");
    }

    [Fact]
    public async Task TheAnsweredAddress_NeverAppearsInEventsSnapshotOrOutput()
    {
        await using var fixture = await StartAsync();
        await fixture.WaitReadyAsync();
        using var events = await fixture.OpenEventsAsync(fixture.Http);
        var login = fixture.PostAsync(fixture.Http, "/input", new { text = "/login account" });
        var interaction = Interaction(await events.WaitForAsync("interaction.opened", IsLogin));

        await fixture.PostAsync(fixture.Http, "/interactions/" + interaction.GetProperty("id").GetString(), new { address = Address });
        await login;
        await events.WaitForAsync("interaction.resolved");
        await events.WaitForAsync("note", static value =>
            value.GetProperty("data").GetProperty("text").GetString() == "Signed in to account.");

        string seen;
        lock (events.Seen)
        {
            seen = string.Join('\n', events.Seen.Select(static value => value.GetRawText()));
        }

        var snapshot = (await fixture.Http.GetStringAsync("/snapshot"));
        Assert.DoesNotContain(Secret, seen, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, fixture.Output + fixture.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnInvalidAnswerLeavesItOpenAndCancelledEndsTheSignIn()
    {
        await using var fixture = await StartAsync();
        await fixture.WaitReadyAsync();
        using var events = await fixture.OpenEventsAsync(fixture.Http);
        var login = fixture.PostAsync(fixture.Http, "/input", new { text = "/login account" });
        var id = Interaction(await events.WaitForAsync("interaction.opened", IsLogin)).GetProperty("id").GetString();

        using var invalid = await fixture.Http.PostAsync("/interactions/" + id, ServerFixture.Body(new { address = "code=abc" }));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        await fixture.PostAsync(fixture.Http, "/interactions/" + id, new { cancelled = true });
        await login;

        var models = (await fixture.GetAsync(fixture.Http, "/models")).GetProperty("models").EnumerateArray()
            .Single(static model => model.GetProperty("provider").GetString() == "account");
        Assert.False(models.GetProperty("credentialReady").GetBoolean());
        await events.WaitForAsync("note", static value =>
            value.GetProperty("data").GetProperty("text").GetString() == "Signing in was cancelled.");
    }

    [Fact]
    public async Task Interrupt_CancelsAWaitingSignInAndTheNextOneRuns()
    {
        await using var fixture = await StartAsync();
        await fixture.WaitReadyAsync();
        using var events = await fixture.OpenEventsAsync(fixture.Http);
        var login = fixture.PostAsync(fixture.Http, "/input", new { text = "/login account" });
        var first = Interaction(await events.WaitForAsync("interaction.opened", IsLogin)).GetProperty("id").GetString();

        var interrupted = await fixture.PostAsync(fixture.Http, "/interrupt", new { });
        await login;

        Assert.True(interrupted.GetProperty("interrupted").GetBoolean());
        await events.WaitForAsync("interaction.cancelled", value => value.GetProperty("data").GetProperty("id").GetString() == first);

        var again = fixture.PostAsync(fixture.Http, "/input", new { text = "/login account" });
        var second = Interaction(await events.WaitForAsync(
            "interaction.opened",
            value => IsLogin(value) && Interaction(value).GetProperty("id").GetString() != first));
        await fixture.PostAsync(fixture.Http, "/interactions/" + second.GetProperty("id").GetString(), new { address = Address });
        await again;
    }

    [Fact]
    public async Task AnsweringASignIn_NeedsTheAdminScope()
    {
        await using var fixture = await StartAsync();
        await fixture.WaitReadyAsync();
        using var events = await fixture.OpenEventsAsync(fixture.Http);
        var issued = await fixture.PostAsync(fixture.Http, "/auth/tokens", new { scope = "operate" });
        using var operate = fixture.Client("operator", issued.GetProperty("token").GetString());
        using var refused = await operate.PostAsync("/input", ServerFixture.Body(new { text = "/login account" }));
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        var login = fixture.PostAsync(fixture.Http, "/input", new { text = "/login account" });
        var id = Interaction(await events.WaitForAsync("interaction.opened", IsLogin)).GetProperty("id").GetString();
        using var answer = await operate.PostAsync("/interactions/" + id, ServerFixture.Body(new { address = Address }));

        Assert.Equal(HttpStatusCode.Forbidden, answer.StatusCode);
        await fixture.PostAsync(fixture.Http, "/interactions/" + id, new { cancelled = true });
        await login;
    }

    [Fact]
    public async Task Info_ListsTheLoginInteractionKind()
    {
        await using var fixture = await StartAsync();
        await fixture.WaitReadyAsync();

        var info = await fixture.GetAsync(fixture.Http, "/info");

        Assert.Contains(info.GetProperty("interactionKinds").EnumerateArray(), static kind => kind.GetString() == "login");
    }
}
