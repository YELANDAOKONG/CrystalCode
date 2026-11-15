using System.Net;
using System.Text;

using CrystalCode.Engine.Authentication;

using Xunit;

namespace CrystalCode.Authentication.Tests.ChatGpt;

public sealed class ChatGptHandlerTests
{
    [Fact]
    public async Task Send_AttachesTheSavedAccessToken()
    {
        using var fixture = new ChatGptFixture();
        await fixture.SignInAsync();
        var inference = new InferenceServer();
        using var client = Client(fixture, inference);

        using var response = await client.PostAsync("https://api.openai.com/v1/responses", Body("one"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Bearer access-1", inference.Authorizations.Single());
        Assert.Single(fixture.Server.TokenRequests);
    }

    [Fact]
    public async Task Send_RefreshesAnExpiringTokenAndSavesTheRotatedPair()
    {
        using var fixture = new ChatGptFixture();
        await fixture.SignInAsync();
        var inference = new InferenceServer();
        using var client = Client(fixture, inference);
        fixture.Clock.Advance(TimeSpan.FromMinutes(59));

        using var response = await client.PostAsync("https://api.openai.com/v1/responses", Body("one"));

        Assert.Equal("Bearer access-2", inference.Authorizations.Single());
        var refresh = fixture.Server.TokenRequests.Last();
        Assert.Equal("refresh_token", refresh["grant_type"]);
        Assert.Equal("refresh-1", refresh["refresh_token"]);
        Assert.Equal(FakeAuthorizationServer.IssuedClientId, refresh["client_id"]);
        Assert.False(refresh.ContainsKey("scope"));
        var record = await fixture.ReadRecordAsync();
        Assert.Equal("access-2", record.AccessToken);
        Assert.Equal("refresh-2", record.RefreshToken);
    }

    [Fact]
    public async Task Send_ReusesAFreshTokenWithoutReadingOrRefreshing()
    {
        using var fixture = new ChatGptFixture();
        await fixture.SignInAsync();
        var inference = new InferenceServer();
        using var client = Client(fixture, inference);

        await client.PostAsync("https://api.openai.com/v1/responses", Body("one"));
        await client.PostAsync("https://api.openai.com/v1/responses", Body("two"));

        Assert.Single(fixture.Server.TokenRequests);
        Assert.Equal(2, inference.Authorizations.Count);
    }

    [Fact]
    public async Task Send_RenewsOnceAfter401AndSendsTheSameBodyAgain()
    {
        using var fixture = new ChatGptFixture();
        await fixture.SignInAsync();
        var inference = new InferenceServer { RejectToken = "access-1" };
        using var client = Client(fixture, inference);

        using var response = await client.PostAsync("https://api.openai.com/v1/responses", Body("payload"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["Bearer access-1", "Bearer access-2"], inference.Authorizations);
        Assert.Equal(["payload", "payload"], inference.Bodies);
        Assert.Equal(1, fixture.Server.RefreshCount);
    }

    [Fact]
    public async Task Send_ReturnsASecond401WithoutLooping()
    {
        using var fixture = new ChatGptFixture();
        await fixture.SignInAsync();
        var inference = new InferenceServer { RejectEverything = true };
        using var client = Client(fixture, inference);

        using var response = await client.PostAsync("https://api.openai.com/v1/responses", Body("x"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(2, inference.Authorizations.Count);
    }

    [Fact]
    public async Task Send_ExplainsARejectedRefreshTokenWithoutLeakingIt()
    {
        using var fixture = new ChatGptFixture();
        await fixture.SignInAsync();
        var record = await fixture.ReadRecordAsync();
        fixture.Server.RefreshError = "invalid_grant";
        using var client = Client(fixture, new InferenceServer());
        fixture.Clock.Advance(TimeSpan.FromMinutes(59));

        var error = await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => client.PostAsync("https://api.openai.com/v1/responses", Body("x")));

        Assert.Contains("crystal login chatgpt-test", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(record.RefreshToken, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Send_RefusesWhenNotSignedIn()
    {
        using var fixture = new ChatGptFixture();
        using var client = Client(fixture, new InferenceServer());

        var error = await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => client.PostAsync("https://api.openai.com/v1/responses", Body("x")));

        Assert.Contains("not signed in", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TwoProcessesSharingTheFile_RefreshOnlyOnce()
    {
        using var fixture = new ChatGptFixture();
        await fixture.SignInAsync();
        fixture.Server.TokenDelay = TimeSpan.FromMilliseconds(150);
        var inference = new InferenceServer();
        using var first = Client(fixture, inference);
        using var second = Client(fixture, inference);
        fixture.Clock.Advance(TimeSpan.FromMinutes(59));

        await Task.WhenAll(
            first.PostAsync("https://api.openai.com/v1/responses", Body("a")),
            second.PostAsync("https://api.openai.com/v1/responses", Body("b")));

        Assert.Equal(1, fixture.Server.RefreshCount);
        Assert.All(inference.Authorizations, value => Assert.Equal("Bearer access-2", value));
    }

    private static HttpClient Client(ChatGptFixture fixture, InferenceServer inference)
    {
        var handler = fixture.Integration.CreateHandler(fixture.Provider, fixture.Vault);
        handler.InnerHandler = inference;
        return new HttpClient(handler);
    }

    private static StringContent Body(string text) =>
        new(text, Encoding.UTF8, "application/json");

    private sealed class InferenceServer : HttpMessageHandler
    {
        public List<string> Authorizations { get; } = [];

        public List<string> Bodies { get; } = [];

        public string? RejectToken { get; set; }

        public bool RejectEverything { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var authorization = request.Headers.Authorization!.ToString();
            lock (Authorizations)
            {
                Authorizations.Add(authorization);
            }

            lock (Bodies)
            {
                Bodies.Add(request.Content is null ? string.Empty : request.Content.ReadAsStringAsync(cancellationToken).Result);
            }

            await Task.Yield();
            return RejectEverything || authorization == $"Bearer {RejectToken}"
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
