using CrystalCode.Engine.Authentication;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Tests.Home;

using Xunit;

namespace CrystalCode.Engine.Tests.Authentication;

public sealed class AuthenticationCredentialStoreTests
{
    private const string Secret = "token-secret-value";

    [Fact]
    public async Task Write_SavesUnderProviderNameAndMarksSignedIn()
    {
        using var root = new TemporaryHome();
        var store = new AuthenticationCredentialStore(root.Home);
        var provider = AuthenticationProvider("account-a", "example");
        Assert.False(store.IsSignedIn(provider));

        var vault = store.Open(provider);
        await vault.WriteAsync($$"""{"refresh":"{{Secret}}"}""", CancellationToken.None);

        Assert.True(store.IsSignedIn(provider));
        Assert.Contains(Secret, await vault.ReadAsync(CancellationToken.None), StringComparison.Ordinal);
        Assert.False(store.IsSignedIn(AuthenticationProvider("account-b", "example")));
    }

    [Fact]
    public async Task IsSignedIn_IsFalseWhenTheIntegrationChanged()
    {
        using var root = new TemporaryHome();
        var store = new AuthenticationCredentialStore(root.Home);
        await store.Open(AuthenticationProvider("account-a", "example"))
            .WriteAsync("{}", CancellationToken.None);

        Assert.False(store.IsSignedIn(AuthenticationProvider("account-a", "other")));
    }

    [Fact]
    public async Task Remove_DeletesOnlyThatProvider()
    {
        using var root = new TemporaryHome();
        var store = new AuthenticationCredentialStore(root.Home);
        var first = AuthenticationProvider("account-a", "example");
        var second = AuthenticationProvider("account-b", "example");
        await store.Open(first).WriteAsync("{}", CancellationToken.None);
        await store.Open(second).WriteAsync("{}", CancellationToken.None);

        Assert.True(await store.RemoveAsync(first.Name, CancellationToken.None));
        Assert.False(await store.RemoveAsync(first.Name, CancellationToken.None));

        Assert.False(store.IsSignedIn(first));
        Assert.True(store.IsSignedIn(second));
    }

    [Fact]
    public async Task RunExclusive_AllowsReadAndWriteInsideTheAction()
    {
        using var root = new TemporaryHome();
        var store = new AuthenticationCredentialStore(root.Home);
        var vault = store.Open(AuthenticationProvider("account-a", "example"));

        var result = await vault.RunExclusiveAsync(
            async token =>
            {
                await vault.WriteAsync("""{"n":1}""", token);
                return await vault.ReadAsync(token);
            },
            CancellationToken.None);

        using var document = System.Text.Json.JsonDocument.Parse(result!);
        Assert.Equal(1, document.RootElement.GetProperty("n").GetInt32());
    }

    [Fact]
    public async Task RunExclusive_SerializesConcurrentActions()
    {
        using var root = new TemporaryHome();
        var store = new AuthenticationCredentialStore(root.Home);
        var vault = store.Open(AuthenticationProvider("account-a", "example"));
        var inside = 0;
        var overlapped = false;

        async Task<int> Work(CancellationToken token)
        {
            if (Interlocked.Increment(ref inside) > 1)
            {
                overlapped = true;
            }

            await Task.Delay(50, token);
            Interlocked.Decrement(ref inside);
            return 0;
        }

        await Task.WhenAll(
            vault.RunExclusiveAsync(Work, CancellationToken.None),
            vault.RunExclusiveAsync(Work, CancellationToken.None));

        Assert.False(overlapped);
    }

    [Fact]
    public async Task Write_CreatesOwnerOnlyFile()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var root = new TemporaryHome();
        var store = new AuthenticationCredentialStore(root.Home);
        await store.Open(AuthenticationProvider("account-a", "example"))
            .WriteAsync("{}", CancellationToken.None);

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            File.GetUnixFileMode(root.Home.AuthenticationCredentialsPath));
    }

    [Fact]
    public async Task CorruptFile_ReportsPathWithoutContents()
    {
        using var root = new TemporaryHome();
        root.Home.EnsureCreated();
        File.WriteAllText(root.Home.AuthenticationCredentialsPath, $"{{ not json {Secret}");
        var store = new AuthenticationCredentialStore(root.Home);

        var error = Assert.Throws<InvalidOperationException>(
            () => store.IsSignedIn(AuthenticationProvider("account-a", "example")));

        Assert.Contains("authentication-credentials.json", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, error.Message, StringComparison.Ordinal);
        await Task.CompletedTask;
    }

    [Fact]
    public void TryResolve_ReportsMissingLoginForAuthenticationProvider()
    {
        using var root = new TemporaryHome();
        var credentials = new CredentialStore(root.Home);

        var resolved = credentials.TryResolve(
            AuthenticationProvider("account-a", "example"),
            out var apiKey,
            out var error);

        Assert.False(resolved);
        Assert.Equal(string.Empty, apiKey);
        Assert.Contains("crystal login account-a", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Workspace_TreatsTheStoreAndItsLockAsCredentialPaths()
    {
        Assert.True(CrystalCode.Engine.Tools.Workspace.IsCredentialPath(
            "/home/x/.crystal/authentication-credentials.json"));
        Assert.True(CrystalCode.Engine.Tools.Workspace.IsCredentialPath(
            "/home/x/.crystal/authentication-credentials.lock"));
    }

    internal static ProviderDefinition AuthenticationProvider(string name, string integration) =>
        new(
            ProviderName.Parse(name),
            ProviderProtocol.Responses,
            new Uri("https://example.test/v1/"),
            new Dictionary<string, ModelSettings> { ["model-a"] = new(64000) },
            type: ProviderType.Authentication,
            integration: integration);
}
