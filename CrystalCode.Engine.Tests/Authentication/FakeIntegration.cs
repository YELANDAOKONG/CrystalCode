using CrystalCode.Engine.Authentication;
using CrystalCode.Engine.Authentication.Interfaces;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Plugins.Interfaces;

namespace CrystalCode.Engine.Tests.Authentication;

/// <summary>
/// An integration with no vendor behind it: its sign-in shows a line, opens
/// one link, and saves a marker, so tests can drive the engine's flow.
/// </summary>
internal sealed class FakeIntegration : IAuthenticationIntegration, IPlugin
{
    public const string Marker = """{"signedIn":true}""";

    public string Id => "fake";

    public string Name => "fake-integration";

    public string? Invalid { get; set; }

    public string? RevokeWarning { get; set; }

    public int Revoked { get; private set; }

    public string? Validate(ProviderDefinition provider) => Invalid;

    public async Task LoginAsync(
        ProviderDefinition provider,
        IAuthenticationVault vault,
        ILoginInteraction interaction,
        CancellationToken cancellationToken)
    {
        await interaction.ShowAsync("Opening the sign-in page.", cancellationToken);
        if (!await interaction.OpenLinkAsync(new Uri("https://example.test/login"), cancellationToken))
        {
            throw new AuthenticationFailedException("The page could not be opened.");
        }

        await vault.WriteAsync(Marker, cancellationToken);
    }

    public DelegatingHandler CreateHandler(ProviderDefinition provider, IAuthenticationVault vault) =>
        new FakeHandler();

    public Task<string?> RevokeAsync(
        ProviderDefinition provider,
        IAuthenticationVault vault,
        CancellationToken cancellationToken)
    {
        Revoked++;
        return Task.FromResult(RevokeWarning);
    }

    public PluginContribution Contribute() => new(integrations: [this]);

    public static ProviderDefinition AccountProvider(string name = "account") =>
        new(
            ProviderName.Parse(name),
            ProviderProtocol.Responses,
            new Uri("https://example.test/v1/"),
            new Dictionary<string, ModelSettings> { ["model-a"] = new(64000) },
            type: ProviderType.Authentication,
            integration: "fake");

    private sealed class FakeHandler : DelegatingHandler
    {
    }
}
