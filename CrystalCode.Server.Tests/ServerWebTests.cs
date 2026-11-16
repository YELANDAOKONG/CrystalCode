using System.Net;
using System.Net.Sockets;
using System.Text;

using CrystalCode.Engine.Home;
using CrystalCode.Engine.Launch;
using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Version;
using CrystalCode.Server.Hosting;
using CrystalCode.Server.Tests.Support;

using Xunit;

namespace CrystalCode.Server.Tests;

public sealed class ServerWebTests
{
    private const string OutsideMarker = "OUTSIDE-MARKER";

    private const string HiddenMarker = "HIDDEN-MARKER";

    private const string ShadowMarker = "SHADOW-MARKER";

    [Fact]
    public async Task Config_IsPublicAndNamesOnlyTheFrontEndFacts()
    {
        await using var fixture = await StartAsync();
        using var anonymous = fixture.Client("anon", token: null);

        var config = await fixture.GetAsync(anonymous, "/config");

        Assert.Equal("crystal-code", config.GetProperty("product").GetString());
        Assert.Equal("/api", config.GetProperty("apiBase").GetString());
        Assert.True(config.GetProperty("authentication").GetBoolean());
        Assert.Equal(["product", "apiBase", "authentication"], config.EnumerateObject().Select(static p => p.Name));
        using var response = await anonymous.GetAsync("/config");
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Config_ReportsAuthenticationOff()
    {
        await using var fixture = await StartAsync(authentication: false);
        using var anonymous = fixture.Client("anon", token: null);

        var config = await fixture.GetAsync(anonymous, "/config");

        Assert.False(config.GetProperty("authentication").GetBoolean());
    }

    [Fact]
    public async Task Api_LivesUnderApiAndStillNeedsAToken()
    {
        await using var fixture = await StartAsync();
        await fixture.WaitReadyAsync();
        using var anonymous = fixture.Client("anon", token: null);

        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/api/health")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/snapshot")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fixture.Http.GetAsync("/api/snapshot")).StatusCode);
        var unknown = await fixture.Http.GetAsync("/api/nope");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("application/problem+json", unknown.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/nope")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await fixture.Http.GetAsync("/api/config")).StatusCode);

        var ticket = await fixture.PostAsync(fixture.Http, "/api/auth/ticket", new { });
        using var stream = await anonymous.GetAsync(
            "/api/events?ticket=" + ticket.GetProperty("ticket").GetString(),
            HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, stream.StatusCode);
    }

    [Fact]
    public async Task Files_ServeWithoutATokenAndWithSafeHeaders()
    {
        await using var fixture = await StartAsync();
        using var anonymous = fixture.Client("anon", token: null);

        using var index = await anonymous.GetAsync("/");
        using var script = await anonymous.GetAsync("/assets/app.js");

        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        Assert.Contains("INDEX", await index.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal("text/html", index.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.OK, script.StatusCode);
        Assert.Contains("javascript", script.Content.Headers.ContentType?.MediaType, StringComparison.Ordinal);
        Assert.Equal("nosniff", script.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-cache", script.Headers.CacheControl?.ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/data.unknownext")).StatusCode);
    }

    [Fact]
    public async Task SpaFallback_AnswersPathsWithoutAnExtensionOnly()
    {
        await using var fixture = await StartAsync();
        using var anonymous = fixture.Client("anon", token: null);

        using var route = await anonymous.GetAsync("/sessions/abc");
        Assert.Equal(HttpStatusCode.OK, route.StatusCode);
        Assert.Contains("INDEX", await route.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/missing.js")).StatusCode);
    }

    [Fact]
    public async Task SpaFallback_CanBeTurnedOff()
    {
        await using var fixture = await StartAsync(spaFallback: false);
        using var anonymous = fixture.Client("anon", token: null);

        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/sessions/abc")).StatusCode);
        using var root = await anonymous.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, root.StatusCode);
        Assert.Contains("INDEX", await root.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Files_NeverShadowTheApiOrConfig()
    {
        await using var fixture = await StartAsync();
        await fixture.WaitReadyAsync();

        var api = await fixture.Http.GetAsync("/api/note.txt");
        var config = await fixture.Http.GetAsync("/config/note.txt");
        var health = await fixture.Http.GetStringAsync("/api/health");

        Assert.Equal(HttpStatusCode.NotFound, api.StatusCode);
        Assert.DoesNotContain(ShadowMarker, await api.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.DoesNotContain(ShadowMarker, await config.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains("state", health, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/../outside.txt")]
    [InlineData("/%2e%2e/outside.txt")]
    [InlineData("/%2E%2E%2Foutside.txt")]
    [InlineData("/..%2foutside.txt")]
    [InlineData("/..%5coutside.txt")]
    [InlineData("/assets/../../outside.txt")]
    [InlineData("/assets/%2e%2e/%2e%2e/outside.txt")]
    [InlineData("/%252e%252e/outside.txt")]
    [InlineData("/....//outside.txt")]
    [InlineData("/assets/..%2f..%2foutside.txt")]
    [InlineData("/%00/../outside.txt")]
    public async Task Traversal_NeverReachesAFileOutsideTheWebDirectory(string target)
    {
        await using var fixture = await StartAsync();
        var (status, response) = await RawAsync(fixture.Server.Url!, target);

        Assert.NotEqual(200, status);
        Assert.DoesNotContain(OutsideMarker, response, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/.env")]
    [InlineData("/.git/config")]
    [InlineData("/%2eenv")]
    [InlineData("/.git%2fconfig")]
    public async Task HiddenFiles_AreNeverServed(string target)
    {
        await using var fixture = await StartAsync();
        var (_, response) = await RawAsync(fixture.Server.Url!, target);

        Assert.DoesNotContain(HiddenMarker, response, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SameOrigin_IsAllowedByDefaultAndOthersAreNot()
    {
        await using var fixture = await StartAsync(origins: ["https://app.example.test"]);
        await fixture.WaitReadyAsync();
        var own = fixture.Server.Url!;

        Assert.Equal(HttpStatusCode.OK, await StatusAsync(fixture, "/api/snapshot", own));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(fixture, "/api/snapshot", "https://app.example.test"));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(fixture, "/api/snapshot", "https://evil.example.test"));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(fixture, "/api/snapshot", "http://127.0.0.1:1"));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(fixture, "/api/snapshot", "null"));
    }

    [Fact]
    public async Task PlainServer_DoesNotAllowItsOwnOriginOrServeFiles()
    {
        await using var fixture = await ServerFixture.StartAsync(new ScriptedModel(false));
        await fixture.WaitReadyAsync();

        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(fixture, "/snapshot", fixture.Server.Url!));
        Assert.Equal(HttpStatusCode.NotFound, (await fixture.Http.GetAsync("/config")).StatusCode);
    }

    [Fact]
    public async Task MissingWebDirectory_StillServesTheApiAndWarns()
    {
        await using var fixture = await ServerFixture.StartAsync(new ScriptedModel(false), web: true);
        await fixture.WaitReadyAsync();
        using var anonymous = fixture.Client("anon", token: null);

        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/config")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fixture.Http.GetAsync("/api/snapshot")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/")).StatusCode);
        Assert.Contains("web directory does not exist", fixture.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WebDirectoryWithoutIndex_Warns()
    {
        await using var fixture = await ServerFixture.StartAsync(
            new ScriptedModel(false),
            prepare: static (home, _) => Directory.CreateDirectory(home.WebDirectory),
            web: true);
        using var anonymous = fixture.Client("anon", token: null);

        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/")).StatusCode);
        Assert.Contains("index.html", fixture.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryCreate_RejectsAnApiBaseInWebMode()
    {
        var options = new ServerOptions
        {
            Launch = new SessionLaunchRequest(new SessionLaunchOptions(), Path.GetTempPath(), Path.GetTempPath()),
            ApiBase = "/other",
            Web = new WebOptions { Root = Path.GetTempPath() }
        };
        var runtime = new ServerRuntime(
            new StringWriter(),
            new StringWriter(),
            new PluginRegistry(),
            new BuildIdentity(null, null, null, null, null, null));

        var created = CrystalServer.TryCreate(options, runtime, out _, out var error);

        Assert.False(created);
        Assert.Contains("/api", error, StringComparison.Ordinal);
    }

    private static Task<ServerFixture> StartAsync(
        bool authentication = true,
        bool spaFallback = true,
        IReadOnlyList<string>? origins = null) =>
        ServerFixture.StartAsync(
            new ScriptedModel(false),
            authentication: authentication,
            origins: origins,
            prepare: Prepare,
            web: true,
            spaFallback: spaFallback);

    private static void Prepare(CrystalHome home, string workspace)
    {
        var web = home.WebDirectory;
        Directory.CreateDirectory(Path.Combine(web, "assets"));
        Directory.CreateDirectory(Path.Combine(web, "api"));
        Directory.CreateDirectory(Path.Combine(web, "config"));
        Directory.CreateDirectory(Path.Combine(web, ".git"));
        File.WriteAllText(Path.Combine(web, "index.html"), "<html>INDEX</html>");
        File.WriteAllText(Path.Combine(web, "assets", "app.js"), "console.log(1);");
        File.WriteAllText(Path.Combine(web, "data.unknownext"), "data");
        File.WriteAllText(Path.Combine(web, ".env"), HiddenMarker);
        File.WriteAllText(Path.Combine(web, ".git", "config"), HiddenMarker);
        File.WriteAllText(Path.Combine(web, "api", "note.txt"), ShadowMarker);
        File.WriteAllText(Path.Combine(web, "config", "note.txt"), ShadowMarker);
        File.WriteAllText(Path.Combine(home.Root, "outside.txt"), OutsideMarker);
    }

    private static async Task<HttpStatusCode> StatusAsync(ServerFixture fixture, string path, string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("Origin", origin);
        using var response = await fixture.Http.SendAsync(request);
        return response.StatusCode;
    }

    /// <summary>
    /// Sends the request target byte for byte. HttpClient would normalize
    /// <c>..</c> and escapes itself and hide what the server does with them.
    /// </summary>
    private static async Task<(int Status, string Response)> RawAsync(string url, string target)
    {
        var uri = new Uri(url);
        using var timeout = new CancellationTokenSource(ServerFixture.Wait);
        using var client = new TcpClient();
        await client.ConnectAsync(uri.Host, uri.Port, timeout.Token);
        var stream = client.GetStream();
        var request = $"GET {target} HTTP/1.1\r\nHost: {uri.Authority}\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request), timeout.Token);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var text = await reader.ReadToEndAsync(timeout.Token);
        var status = int.Parse(text.Split(' ', 3)[1], System.Globalization.CultureInfo.InvariantCulture);
        return (status, text);
    }
}
