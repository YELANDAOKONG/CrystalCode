using System.Net;
using System.Text.Json;

using CrystalCode.Server.Security;
using CrystalCode.Server.Tests.Support;

using Xunit;

namespace CrystalCode.Server.Tests;

public sealed class ServerSecurityTests
{
    [Fact]
    public async Task Requests_NeedATokenExceptHealth()
    {
        await using var fixture = await ServerFixture.StartAsync(new ScriptedModel(false));
        await fixture.WaitReadyAsync();
        using var anonymous = fixture.Client("anon", token: null);
        using var wrong = fixture.Client("wrong", token: "not-the-token-at-all");

        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/health")).StatusCode);
        var denied = await anonymous.GetAsync("/snapshot");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.Equal("application/problem+json", denied.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.Unauthorized, (await wrong.GetAsync("/snapshot")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fixture.Http.GetAsync("/snapshot")).StatusCode);
    }

    [Fact]
    public async Task AuthNone_AllowsRequestsAndWarns()
    {
        await using var fixture = await ServerFixture.StartAsync(new ScriptedModel(false), authentication: false);
        await fixture.WaitReadyAsync();
        using var anonymous = fixture.Client("anon", token: null);

        var info = await fixture.GetAsync(anonymous, "/info");

        Assert.False(info.GetProperty("authentication").GetBoolean());
        Assert.Equal("admin", info.GetProperty("scope").GetString());
        Assert.Contains("authentication is off", fixture.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scopes_LimitWhatATokenMayDo()
    {
        await using var fixture = await ServerFixture.StartAsync(new ScriptedModel(false));
        await fixture.WaitReadyAsync();
        var observer = await fixture.PostAsync(fixture.Http, "/auth/tokens", new { scope = "observe", label = "watch" });
        var operatorToken = await fixture.PostAsync(fixture.Http, "/auth/tokens", new { scope = "operate" });
        using var watch = fixture.Client("watch", observer.GetProperty("token").GetString());
        using var work = fixture.Client("work", operatorToken.GetProperty("token").GetString());

        Assert.Equal(HttpStatusCode.OK, (await watch.GetAsync("/snapshot")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await watch.PostAsync("/input", ServerFixture.Body(new { text = "hi" }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await work.PostAsync("/input", ServerFixture.Body(new { text = "/status" }))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await work.PostAsync("/input", ServerFixture.Body(new { text = "/approval full" }))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await work.PostAsync("/shutdown", ServerFixture.Body(new { }))).StatusCode);

        await fixture.Http.DeleteAsync("/auth/tokens/" + observer.GetProperty("id").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await watch.GetAsync("/snapshot")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await fixture.Http.DeleteAsync("/auth/tokens/" + TokenStore.PrimaryId)).StatusCode);
    }

    [Fact]
    public async Task Ticket_OpensTheStreamOnce()
    {
        await using var fixture = await ServerFixture.StartAsync(new ScriptedModel(false));
        await fixture.WaitReadyAsync();
        var ticket = (await fixture.PostAsync(fixture.Http, "/auth/ticket", new { })).GetProperty("ticket").GetString();
        using var browser = fixture.Client("browser", token: null);

        using var first = await browser.GetAsync("/events?ticket=" + ticket, HttpCompletionOption.ResponseHeadersRead);
        using var second = await browser.GetAsync("/events?ticket=" + ticket, HttpCompletionOption.ResponseHeadersRead);
        using var elsewhere = await browser.GetAsync("/snapshot?ticket=" + ticket);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("text/event-stream", first.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, elsewhere.StatusCode);
    }

    [Fact]
    public async Task HostAndOrigin_AreChecked()
    {
        await using var fixture = await ServerFixture.StartAsync(new ScriptedModel(false), origins: ["https://app.example.test"]);
        await fixture.WaitReadyAsync();

        var rebound = new HttpRequestMessage(HttpMethod.Get, "/health");
        rebound.Headers.Host = "attacker.example.test";
        var foreign = new HttpRequestMessage(HttpMethod.Get, "/snapshot");
        foreign.Headers.Add("Origin", "https://evil.example.test");
        var allowed = new HttpRequestMessage(HttpMethod.Get, "/snapshot");
        allowed.Headers.Add("Origin", "https://app.example.test");

        Assert.Equal(HttpStatusCode.MisdirectedRequest, (await fixture.Http.SendAsync(rebound)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.Http.SendAsync(foreign)).StatusCode);
        var ok = await fixture.Http.SendAsync(allowed);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("https://app.example.test", ok.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public void TokenStore_ComparesHashesAndExpiresTickets()
    {
        var time = new ManualTime(DateTimeOffset.UnixEpoch);
        var store = new TokenStore("primary-secret-value", time);
        Assert.True(store.TryAuthenticate("primary-secret-value", out var primary));
        Assert.False(store.TryAuthenticate("primary-secret-valuf", out _));

        var ticket = store.IssueTicket(primary);
        time.Advance(TimeSpan.FromSeconds(31));

        Assert.False(store.TryRedeemTicket(ticket, out _));
        Assert.DoesNotContain(store.List(), token => token.Label.Contains("secret", StringComparison.Ordinal));
    }

    [Fact]
    public void Scopes_CoverEverySlashVerb()
    {
        foreach (var verb in Enum.GetValues<Engine.Sessions.SessionVerb>())
        {
            // ForVerb throws for a verb it does not classify.
            var scope = ServerScopes.ForVerb(verb);
            Assert.True(Enum.IsDefined(scope));
        }

        Assert.Equal(ServerScope.Operate, ServerScopes.ForInput("plain text"));
        Assert.Equal(ServerScope.Operate, ServerScopes.ForInput("/status full"));
        Assert.Equal(ServerScope.Admin, ServerScopes.ForInput("/approval full"));
        Assert.Equal(ServerScope.Admin, ServerScopes.ForInput("/cd .."));
        Assert.Equal(ServerScope.Operate, ServerScopes.ForInput("/cd"));
        Assert.Equal(ServerScope.Admin, ServerScopes.ForInput("/quit"));
    }

    private sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
