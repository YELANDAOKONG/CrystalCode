using CrystalCode.Engine.Authentication.Interfaces;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Plugins.Interfaces;

namespace CrystalCode.Bridge.Tests.Support;

/// <summary>
/// An account integration with no vendor behind it. Its sign-in shows the
/// link, waits for the pasted address, and saves a marker.
/// </summary>
internal sealed class LoginFakeIntegration : IAuthenticationIntegration, IPlugin
{
    public string Id => "fake";

    public string Name => "fake-integration";

    public string? Validate(ProviderDefinition provider) => null;

    public async Task LoginAsync(
        ProviderDefinition provider,
        IAuthenticationVault vault,
        ILoginInteraction interaction,
        CancellationToken cancellationToken)
    {
        await interaction.ShowAsync("Continue in your browser.", cancellationToken);
        var link = new Uri("https://example.test/authorize?state=abc");
        if (!await interaction.OpenLinkAsync(link, cancellationToken))
        {
            await interaction.ShowLinkAsync("Open this address:", link, cancellationToken);
        }

        await interaction.ReadAsync("Paste the address it ended on:", cancellationToken);
        await vault.WriteAsync("""{"signedIn":true}""", cancellationToken);
    }

    public DelegatingHandler CreateHandler(ProviderDefinition provider, IAuthenticationVault vault) =>
        new PassThroughHandler();

    public PluginContribution Contribute() => new(integrations: [this]);

    public static void WriteProviders(string home) =>
        File.WriteAllText(
            Path.Combine(home, "providers.json"),
            """
            {
              "account": {
                "type": "authentication",
                "integration": "fake",
                "protocol": "responses",
                "baseUri": "https://example.test/v1/",
                "models": { "model-a": { "contextWindow": 64000 } }
              }
            }
            """);

    private sealed class PassThroughHandler : DelegatingHandler
    {
    }
}
