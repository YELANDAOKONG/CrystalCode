using CrystalCode.Engine.Authentication;
using CrystalCode.Engine.Authentication.Interfaces;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Plugins.Disk;
using CrystalCode.Engine.Tests.Authentication;
using CrystalCode.Engine.Tests.Home;
using CrystalCode.Engine.Tests.Tools;
using CrystalCode.Engine.Tools;
using CrystalCode.Plugins.Clients;

using Xunit;

namespace CrystalCode.Engine.Tests.Plugins;

public sealed class PluginAuthenticationTests : IClassFixture<PluginAuthenticationFixture>
{
    private readonly PluginAuthenticationFixture _fixture;

    public PluginAuthenticationTests(PluginAuthenticationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Load_RegistersTheAccountLoginAndOmitsABadOne()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        _fixture.Install(home.Home.PluginsDirectory);

        var catalog = PluginCatalog.Load(home.Home, new Workspace(workspace.Path), enabled: true);

        Assert.Equal("plug-account", Assert.Single(catalog.Integrations).Id);
        Assert.NotNull(catalog.FindIntegration("plug-account"));
        Assert.Null(catalog.FindIntegration("1-bad"));
        Assert.Contains(catalog.Notes, note => note.Contains("registered the account login 'plug-account'", StringComparison.Ordinal));
        Assert.Contains(catalog.Notes, note => note.Contains("missing, invalid, or repeated id", StringComparison.Ordinal));
        Assert.Contains(catalog.Notes, note => note.Contains("missing account login", StringComparison.Ordinal));
    }

    [Fact]
    public void HomeOnly_SkipsThePluginsOfTheWorkspace()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        _fixture.Install(Path.Combine(workspace.Path, ".crystal", "plugins"));

        var all = PluginCatalog.Load(home.Home, new Workspace(workspace.Path), enabled: true);
        var homeOnly = PluginCatalog.Load(home.Home, new Workspace(workspace.Path), enabled: true, homeOnly: true);

        Assert.Single(all.Integrations);
        Assert.Empty(homeOnly.Integrations);
    }

    [Fact]
    public async Task SignInAndOut_RunThroughThePluginWithItsTextAndWarning()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        _fixture.Install(home.Home.PluginsDirectory);
        var catalog = PluginCatalog.Load(home.Home, new Workspace(workspace.Path), enabled: true);
        var credentials = new CredentialStore(home.Home);
        var provider = Account("plug", "plug-proto");
        var signIn = new AccountSignIn(credentials, catalog.FindIntegration);
        var login = new ScriptedLogin(opensBrowser: false);

        await signIn.LoginAsync(provider, login, CancellationToken.None);

        Assert.Contains("plugin sign-in for plug", login.Shown);
        Assert.Contains("Open this address: https://example.test/signin", login.Shown);
        Assert.True(credentials.Authentication.IsSignedIn(provider));

        var result = await signIn.LogoutAsync(provider, CancellationToken.None);

        Assert.True(result.Removed);
        Assert.Equal("plugin revoke warning", result.Warning);
        Assert.False(credentials.Authentication.IsSignedIn(provider));
    }

    [Fact]
    public async Task APluginFailure_ShowsItsOwnTextOrThePluginTypeAndMessage()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        _fixture.Install(home.Home.PluginsDirectory);
        var catalog = PluginCatalog.Load(home.Home, new Workspace(workspace.Path), enabled: true);
        var signIn = new AccountSignIn(new CredentialStore(home.Home), catalog.FindIntegration);

        var polite = await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => signIn.LoginAsync(Account("polite", "plug-proto"), new ScriptedLogin(), CancellationToken.None));
        var boom = await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => signIn.LoginAsync(Account("boom", "plug-proto"), new ScriptedLogin(), CancellationToken.None));

        Assert.Equal("Polite failure.", polite.Message);
        Assert.Equal(
            "Plugin sign-in 'AuthFixture' could not sign in (InvalidOperationException): secret-token-xyz",
            boom.Message);
    }

    [Fact]
    public async Task TheClientFactory_ReceivesTheHandlerAndNoKey()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        _fixture.Install(home.Home.PluginsDirectory);
        var catalog = PluginCatalog.Load(home.Home, new Workspace(workspace.Path), enabled: true);
        var credentials = new CredentialStore(home.Home);
        var provider = Account("plug", "plug-proto");
        await new AccountSignIn(credentials, catalog.FindIntegration)
            .LoginAsync(provider, new ScriptedLogin(), CancellationToken.None);
        Assert.True(credentials.TryOpen(provider, catalog.FindIntegration, out var credential, out _));
        Assert.NotNull(credential.Handler);

        var usage = new List<ProviderRequestUsage>();
        var error = Assert.Throws<InvalidOperationException>(() =>
            catalog.TryCreateClient(Settings(provider), credential, () => new("session", null, usage.Add), out _));

        Assert.Equal("sent:plug-token key=0", error.Message);
        Assert.Equal(new ProviderRequestUsage(1, InputTokens: 100, CacheReadTokens: 60), Assert.Single(usage));
    }

    [Fact]
    public void TheIntegrationCheck_RefusesAnAddressItDoesNotServe()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        _fixture.Install(home.Home.PluginsDirectory);
        var catalog = PluginCatalog.Load(home.Home, new Workspace(workspace.Path), enabled: true);
        var credentials = new CredentialStore(home.Home);
        var elsewhere = new ProviderDefinition(
            ProviderName.Parse("plug"),
            ProviderProtocol.Parse("plug-proto"),
            new Uri("https://other.test/v1/"),
            new Dictionary<string, ModelSettings> { ["m"] = new(1000) },
            type: ProviderType.Authentication,
            integration: "plug-account");
        credentials.Authentication.Open(elsewhere).WriteAsync("{}", CancellationToken.None).GetAwaiter().GetResult();

        var opened = credentials.TryOpen(elsewhere, catalog.FindIntegration, out _, out var error);

        Assert.False(opened);
        Assert.Contains("not the plugin's service", error, StringComparison.Ordinal);
    }

    private static ProviderDefinition Account(string name, string protocol) =>
        new(
            ProviderName.Parse(name),
            ProviderProtocol.Parse(protocol),
            new Uri("https://example.test/v1/"),
            new Dictionary<string, ModelSettings> { ["m"] = new(1000) },
            type: ProviderType.Authentication,
            integration: "plug-account");

    private static HarnessSettings Settings(ProviderDefinition provider) =>
        new(
            provider.Name,
            "m",
            CrystalCode.Engine.Approvals.ApprovalMode.Default,
            HarnessSettings.DefaultCompactionThreshold,
            new ProviderCatalog([provider]));
}
