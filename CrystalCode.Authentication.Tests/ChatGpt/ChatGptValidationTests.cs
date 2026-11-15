using System.Security.Cryptography;
using System.Text.Json;

using CrystalCode.Authentication.ChatGpt;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Home;

using Xunit;

namespace CrystalCode.Authentication.Tests.ChatGpt;

public sealed class ChatGptValidationTests
{
    private static readonly ChatGptIntegration Integration = new();

    [Fact]
    public void Validate_AcceptsTheResourceAddress()
    {
        Assert.Null(Integration.Validate(ChatGptFixture.CreateProvider("a")));
    }

    [Theory]
    [InlineData("https://example.test/v1/")]
    [InlineData("https://api.openai.com/v2/")]
    [InlineData("http://api.openai.com/v1/")]
    [InlineData("https://chatgpt.com/backend-api/")]
    public void Validate_RefusesToSendTheTokenToAnotherAddress(string address)
    {
        var provider = ChatGptFixture.CreateProvider("a", new Uri(address));

        Assert.Contains("only to", Integration.Validate(provider), StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RequiresTheResponsesProtocol()
    {
        var provider = ChatGptFixture.CreateProvider("a", protocol: ProviderProtocol.OpenAI);

        Assert.Contains("responses", Integration.Validate(provider), StringComparison.Ordinal);
    }

    [Fact]
    public void HostIdentity_IsStableAndHidesTheNames()
    {
        var first = HostIdentity.Create("machine", "user");

        Assert.Equal(first, HostIdentity.Create("machine", "user"));
        Assert.NotEqual(first, HostIdentity.Create("machine", "other"));
        Assert.DoesNotContain("machine", first, StringComparison.Ordinal);
        Assert.True(Guid.TryParse(first["urn:uuid:".Length..], out _));
    }

    [Fact]
    public async Task IdToken_RejectsAWrongKeyIssuerNonceAndExpiry()
    {
        var server = new FakeAuthorizationServer();
        var validator = new IdTokenValidator(new HttpClient(server), TimeProvider.System);
        server.TokenSubject = "s";
        using var stranger = RSA.Create(2048);
        var cases = new[]
        {
            server.SignIdToken(nonce: "n", key: stranger),
            server.SignIdToken(nonce: "n", issuer: "https://evil.example"),
            server.SignIdToken(nonce: "other"),
            server.SignIdToken(nonce: "n", expires: DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds())
        };

        foreach (var token in cases)
        {
            await Assert.ThrowsAsync<CrystalCode.Engine.Authentication.AuthenticationFailedException>(
                () => validator.ValidateAsync(token, FakeAuthorizationServer.IssuedClientId, "n", CancellationToken.None));
        }

        var identity = await validator.ValidateAsync(
            server.SignIdToken(nonce: "n"),
            FakeAuthorizationServer.IssuedClientId,
            "n",
            CancellationToken.None);
        Assert.Equal("s", identity.Subject);
    }

    [Fact]
    public async Task Engine_ResolvesAnAccountProviderToAHandlerOnlyWhenSignedIn()
    {
        using var fixture = new ChatGptFixture();
        var registry = new PluginRegistry();
        registry.Add(new ChatGptPlugin());
        var credentials = new CredentialStore(fixture.Home);

        Assert.False(credentials.TryOpen(fixture.Provider, registry, out _, out var error));
        Assert.Contains("crystal login chatgpt-test", error, StringComparison.Ordinal);

        await fixture.SignInAsync();

        Assert.True(credentials.TryOpen(fixture.Provider, registry, out var credential, out _));
        Assert.NotNull(credential.Handler);
        Assert.Equal(string.Empty, credential.ApiKey);
    }

    [Fact]
    public async Task Engine_RefusesAnAccountProviderWhoseAddressIsNotTheResource()
    {
        using var fixture = new ChatGptFixture();
        await fixture.SignInAsync();
        var registry = new PluginRegistry();
        registry.Add(new ChatGptPlugin());
        var moved = ChatGptFixture.CreateProvider("chatgpt-test", new Uri("https://example.test/v1/"));

        var opened = new CredentialStore(fixture.Home).TryOpen(moved, registry, out _, out var error);

        Assert.False(opened);
        Assert.Contains("only to", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Engine_ReportsAMissingIntegration()
    {
        using var fixture = new ChatGptFixture();
        var provider = ChatGptFixture.CreateProvider("chatgpt-test");
        fixture.Store.Open(provider).WriteAsync("{}", CancellationToken.None).GetAwaiter().GetResult();

        var opened = new CredentialStore(fixture.Home).TryOpen(provider, new PluginRegistry(), out _, out var error);

        Assert.False(opened);
        Assert.Contains("not registered", error, StringComparison.Ordinal);
    }
}
