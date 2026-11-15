using CrystalCode.Authentication.ChatGpt;
using CrystalCode.Engine.Authentication;

using Xunit;

namespace CrystalCode.Authentication.Tests.ChatGpt;

public sealed class ChatGptLoginTests
{
    [Fact]
    public async Task FirstSignIn_RegistersAndSavesTheLogin()
    {
        using var fixture = new ChatGptFixture();
        var interaction = new ScriptedLoginInteraction(fixture.Server);

        await fixture.SignInAsync(interaction);

        Assert.Equal("dynamic_agent_client", interaction.Query("client_id"));
        Assert.Equal("Crystal Code", interaction.Query("agent_name_hint"));
        Assert.StartsWith("urn:uuid:", interaction.Query("ext_agent_host_id"), StringComparison.Ordinal);
        Assert.Equal("https://api.openai.com/v1", interaction.Query("resource"));
        Assert.Equal("S256", interaction.Query("code_challenge_method"));
        Assert.Contains("chatgpt.tokens.use.direct", interaction.Query("scope"), StringComparison.Ordinal);
        Assert.StartsWith("http://127.0.0.1:", interaction.Query("redirect_uri"), StringComparison.Ordinal);
        Assert.EndsWith("/auth/callback", interaction.Query("redirect_uri"), StringComparison.Ordinal);

        var exchange = Assert.Single(fixture.Server.TokenRequests);
        Assert.Equal("authorization_code", exchange["grant_type"]);
        Assert.Equal(FakeAuthorizationServer.IssuedClientId, exchange["client_id"]);
        Assert.Equal(interaction.Query("redirect_uri"), exchange["redirect_uri"]);
        Assert.Equal("https://api.openai.com/v1", exchange["resource"]);
        Assert.Equal(
            interaction.Query("code_challenge"),
            Pkce.Base64Url(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.ASCII.GetBytes(exchange["code_verifier"]))));

        var record = await fixture.ReadRecordAsync();
        Assert.Equal(FakeAuthorizationServer.IssuedClientId, record.ClientId);
        Assert.Equal(FakeAuthorizationServer.Subject, record.Subject);
        Assert.Equal("person@example.test", record.Email);
        Assert.True(fixture.Store.IsSignedIn(fixture.Provider));
    }

    [Fact]
    public async Task SecondSignIn_ReusesTheIssuedClientAndSkipsTheNameHint()
    {
        using var fixture = new ChatGptFixture();
        await fixture.SignInAsync();

        var again = new ScriptedLoginInteraction(fixture.Server);
        await fixture.SignInAsync(again);

        Assert.Equal(FakeAuthorizationServer.IssuedClientId, again.Query("client_id"));
        Assert.Null(System.Web.HttpUtility.ParseQueryString(again.OpenedLink!.Query)["agent_name_hint"]);
    }

    [Fact]
    public async Task PastedAddress_CompletesTheSignInWhenNoBrowserOpens()
    {
        using var fixture = new ChatGptFixture();
        var interaction = new ScriptedLoginInteraction(fixture.Server)
        {
            BrowserWorks = false,
            PasteWhenAsked = true
        };

        await fixture.SignInAsync(interaction);

        Assert.True(fixture.Store.IsSignedIn(fixture.Provider));
        Assert.Contains(interaction.Shown, text => text.Contains("Open this address", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StateMismatch_IsRejectedWithoutExchangingTheCode()
    {
        using var fixture = new ChatGptFixture();
        var interaction = new ScriptedLoginInteraction(fixture.Server) { CorruptState = true };

        await Assert.ThrowsAsync<AuthenticationFailedException>(() => fixture.SignInAsync(interaction));

        Assert.Empty(fixture.Server.TokenRequests);
        Assert.False(fixture.Store.IsSignedIn(fixture.Provider));
    }

    [Fact]
    public async Task AccessDenied_ExplainsPlanUsageAndExchangesNothing()
    {
        using var fixture = new ChatGptFixture();
        var interaction = new ScriptedLoginInteraction(fixture.Server) { ErrorInCallback = "access_denied" };

        var error = await Assert.ThrowsAsync<AuthenticationFailedException>(() => fixture.SignInAsync(interaction));

        Assert.Contains("plan usage", error.Message, StringComparison.Ordinal);
        Assert.Empty(fixture.Server.TokenRequests);
    }

    [Fact]
    public async Task MissingPlanScope_IsRejectedAndNothingIsSaved()
    {
        using var fixture = new ChatGptFixture();
        fixture.Server.Scope = "openid profile email offline_access";

        var error = await Assert.ThrowsAsync<AuthenticationFailedException>(() => fixture.SignInAsync());

        Assert.Contains("plan usage", error.Message, StringComparison.Ordinal);
        Assert.False(fixture.Store.IsSignedIn(fixture.Provider));
    }

    [Fact]
    public async Task RegistrationWithoutAnIssuedClient_IsRejected()
    {
        using var fixture = new ChatGptFixture();
        var interaction = new ScriptedLoginInteraction(fixture.Server) { ClientIdInCallback = "dynamic_agent_client" };

        await Assert.ThrowsAsync<AuthenticationFailedException>(() => fixture.SignInAsync(interaction));
    }

    [Fact]
    public async Task DifferentAccount_DoesNotReplaceTheSavedLogin()
    {
        using var fixture = new ChatGptFixture();
        await fixture.SignInAsync();
        fixture.Server.TokenSubject = "someone-else";

        var error = await Assert.ThrowsAsync<AuthenticationFailedException>(() => fixture.SignInAsync());

        Assert.Contains("different ChatGPT account", error.Message, StringComparison.Ordinal);
        Assert.Equal(FakeAuthorizationServer.Subject, (await fixture.ReadRecordAsync()).Subject);
    }

    [Fact]
    public async Task ForgedIdToken_IsRejected()
    {
        using var fixture = new ChatGptFixture();
        // Audience for another client: the signature is fine but the token is not ours.
        fixture.Server.TokenAudience = "someone-elses-client";

        await Assert.ThrowsAsync<AuthenticationFailedException>(() => fixture.SignInAsync());

        Assert.False(fixture.Store.IsSignedIn(fixture.Provider));
    }

    [Fact]
    public async Task Messages_NeverContainTokensOrTheCode()
    {
        using var fixture = new ChatGptFixture();
        var interaction = new ScriptedLoginInteraction(fixture.Server);
        await fixture.SignInAsync(interaction);
        var record = await fixture.ReadRecordAsync();

        var everything = string.Join('\n', interaction.Shown) + record + fixture.Server.TokenRequests.Count;
        Assert.DoesNotContain(record.AccessToken, everything, StringComparison.Ordinal);
        Assert.DoesNotContain(record.RefreshToken, everything, StringComparison.Ordinal);
        Assert.DoesNotContain("the-code", everything, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SavedFile_HoldsTheLoginUnderTheProviderNameOnly()
    {
        using var fixture = new ChatGptFixture();
        await fixture.SignInAsync();

        var file = fixture.RawFile();

        Assert.Contains("chatgpt-test", file, StringComparison.Ordinal);
        Assert.Contains("\"integration\": \"chatgpt\"", file, StringComparison.Ordinal);
        Assert.Contains("refresh_token", file, StringComparison.Ordinal);
    }
}
