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

        var health = await anonymous.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        using (var body = JsonDocument.Parse(await health.Content.ReadAsStringAsync()))
        {
            var started = body.RootElement.GetProperty("startedUtc").GetDateTimeOffset();
            Assert.Equal(TimeSpan.Zero, started.Offset);
            Assert.InRange(started, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow);
            var info = await fixture.GetAsync(fixture.Http, "/info");
            Assert.Equal(started, info.GetProperty("startedUtc").GetDateTimeOffset());
        }

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
    public async Task RejectedHost_StillCarriesCorsHeadersForAnAllowedOrigin()
    {
        await using var fixture = await ServerFixture.StartAsync(new ScriptedModel(false), origins: ["https://app.example.test"]);
        await fixture.WaitReadyAsync();

        var allowed = new HttpRequestMessage(HttpMethod.Get, "/health");
        allowed.Headers.Host = "attacker.example.test";
        allowed.Headers.Add("Origin", "https://app.example.test");
        var foreign = new HttpRequestMessage(HttpMethod.Get, "/health");
        foreign.Headers.Host = "attacker.example.test";
        foreign.Headers.Add("Origin", "https://evil.example.test");

        var misdirected = await fixture.Http.SendAsync(allowed);
        var refused = await fixture.Http.SendAsync(foreign);

        Assert.Equal(HttpStatusCode.MisdirectedRequest, misdirected.StatusCode);
        Assert.Equal("https://app.example.test", misdirected.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal(HttpStatusCode.MisdirectedRequest, refused.StatusCode);
        Assert.False(refused.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(refused.Headers.Contains("Access-Control-Expose-Headers"));
    }

    [Fact]
    public async Task ForbiddenOrigin_NeverGetsCorsHeaders()
    {
        await using var fixture = await ServerFixture.StartAsync(new ScriptedModel(false), origins: ["https://app.example.test"]);
        await fixture.WaitReadyAsync();
        var foreign = new HttpRequestMessage(HttpMethod.Get, "/snapshot");
        foreign.Headers.Add("Origin", "https://evil.example.test");

        var response = await fixture.Http.SendAsync(foreign);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Expose-Headers"));
    }

    [Fact]
    public async Task AllowedOrigin_ExposesTheDefaultAndConfiguredHeaders()
    {
        await using var fixture = await ServerFixture.StartAsync(
            new ScriptedModel(false),
            origins: ["https://app.example.test"],
            exposedHeaders: ["X-Request-Id", "retry-after"]);
        await fixture.WaitReadyAsync();
        using var anonymous = fixture.Client("anon", token: null);
        var request = new HttpRequestMessage(HttpMethod.Get, "/snapshot");
        request.Headers.Add("Origin", "https://app.example.test");

        var unauthorized = await anonymous.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal("https://app.example.test", unauthorized.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal(
            "Retry-After, Content-Disposition, WWW-Authenticate, X-Request-Id",
            unauthorized.Headers.GetValues("Access-Control-Expose-Headers").Single());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Preflight_AnswersPrivateNetworkAccessOnlyWhenConfigured(bool configured, bool expected)
    {
        await using var fixture = await ServerFixture.StartAsync(
            new ScriptedModel(false),
            origins: ["https://app.example.test"],
            allowPrivateNetwork: configured);
        await fixture.WaitReadyAsync();
        var preflight = new HttpRequestMessage(HttpMethod.Options, "/input");
        preflight.Headers.Add("Origin", "https://app.example.test");
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        preflight.Headers.Add("Access-Control-Request-Private-Network", "true");

        var response = await fixture.Http.SendAsync(preflight);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(expected, response.Headers.Contains("Access-Control-Allow-Private-Network"));
    }

    [Fact]
    public async Task WildcardOrigin_AllowsAnyPageAndWarns()
    {
        await using var fixture = await ServerFixture.StartAsync(new ScriptedModel(false), origins: ["*"]);
        await fixture.WaitReadyAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, "/snapshot");
        request.Headers.Add("Origin", "https://anything.example.test");

        var response = await fixture.Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("https://anything.example.test", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Contains("any web page", fixture.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("192.168.0.205:8080")]
    [InlineData("[fe80::1]:8080")]
    public async Task IpLiteralHost_PassesThePreflight(string host)
    {
        await using var fixture = await ServerFixture.StartAsync(new ScriptedModel(false), origins: ["*"]);
        await fixture.WaitReadyAsync();
        var preflight = new HttpRequestMessage(HttpMethod.Options, "/snapshot");
        preflight.Headers.Host = host;
        preflight.Headers.Add("Origin", "https://anything.example.test");
        preflight.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await fixture.Http.SendAsync(preflight);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task WildcardOrigin_StillChecksTheHostAndTheToken()
    {
        await using var fixture = await ServerFixture.StartAsync(new ScriptedModel(false), origins: ["*"]);
        await fixture.WaitReadyAsync();
        using var anonymous = fixture.Client("anon", token: null);
        var unauthorized = new HttpRequestMessage(HttpMethod.Get, "/snapshot");
        unauthorized.Headers.Add("Origin", "https://anything.example.test");
        var rebound = new HttpRequestMessage(HttpMethod.Get, "/health");
        rebound.Headers.Host = "attacker.example.test";
        rebound.Headers.Add("Origin", "https://anything.example.test");

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(unauthorized)).StatusCode);
        Assert.Equal(HttpStatusCode.MisdirectedRequest, (await fixture.Http.SendAsync(rebound)).StatusCode);
    }

    [Fact]
    public async Task Health_StillNeedsAnAllowedHostAndOrigin()
    {
        await using var fixture = await ServerFixture.StartAsync(new ScriptedModel(false), origins: ["https://app.example.test"]);
        await fixture.WaitReadyAsync();
        var foreign = new HttpRequestMessage(HttpMethod.Get, "/health");
        foreign.Headers.Add("Origin", "https://evil.example.test");

        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.Http.SendAsync(foreign)).StatusCode);
    }

    [Fact]
    public void TryCreate_RejectsAMalformedOriginOrHeaderName()
    {
        var runtime = new Hosting.ServerRuntime(
            TextWriter.Null,
            TextWriter.Null,
            new Engine.Plugins.PluginRegistry(),
            new Engine.Version.BuildIdentity(null, null, null, null, null, null));
        var launch = new Engine.Launch.SessionLaunchRequest(new Engine.Launch.SessionLaunchOptions(), ".", ".");

        var badOrigin = Hosting.CrystalServer.TryCreate(
            new Hosting.ServerOptions { Launch = launch, AllowedOrigins = ["https://app.example.test/path"] },
            runtime,
            out _,
            out var originError);
        var badHeader = Hosting.CrystalServer.TryCreate(
            new Hosting.ServerOptions { Launch = launch, ExposedHeaders = ["Bad Header"] },
            runtime,
            out _,
            out var headerError);

        Assert.False(badOrigin);
        Assert.Contains("https://app.example.test/path", originError, StringComparison.Ordinal);
        Assert.False(badHeader);
        Assert.Contains("Bad Header", headerError, StringComparison.Ordinal);
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
        Assert.Equal(ServerScope.Operate, ServerScopes.ForInput("/notify"));
        Assert.Equal(ServerScope.Admin, ServerScopes.ForInput("/notify test"));
        Assert.Equal(ServerScope.Operate, ServerScopes.ForInput("/topic"));
        Assert.Equal(ServerScope.Admin, ServerScopes.ForInput("/topic Rename it"));
        Assert.Equal(ServerScope.Admin, ServerScopes.ForInput("/topic off"));
        Assert.Equal(ServerScope.Operate, ServerScopes.ForInput("/describe"));
        Assert.Equal(ServerScope.Operate, ServerScopes.ForInput("/describe audio"));
        Assert.Equal(ServerScope.Operate, ServerScopes.ForInput("/describe image model"));
        Assert.Equal(ServerScope.Admin, ServerScopes.ForInput("/describe image off"));
        Assert.Equal(ServerScope.Admin, ServerScopes.ForInput("/describe video model scripted reel"));
    }

    private sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
