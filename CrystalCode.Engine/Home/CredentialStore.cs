using System.Text.Json;
using CrystalCode.Engine.Authentication;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Plugins;

namespace CrystalCode.Engine.Home;

/// <summary>
/// Resolves provider API keys from the environment, then credentials.json.
/// </summary>
public sealed class CredentialStore
{
    public const string SharedApiKeyVariable = "CRYSTAL_API_KEY";

    private readonly CrystalHome _home;

    public CredentialStore(CrystalHome home)
    {
        ArgumentNullException.ThrowIfNull(home);
        _home = home;
        Authentication = new AuthenticationCredentialStore(home);
    }

    /// <summary>
    /// Gets the saved account logins for authentication providers.
    /// </summary>
    public AuthenticationCredentialStore Authentication { get; }

    /// <summary>
    /// Resolves what a client needs. An authentication provider needs a saved
    /// login and a registered integration; this never touches the network.
    /// </summary>
    public bool TryOpen(
        ProviderDefinition provider,
        PluginRegistry plugins,
        out ProviderCredential credential,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(plugins);
        credential = new ProviderCredential(string.Empty);
        if (provider.Type != ProviderType.Authentication)
        {
            if (!TryResolve(provider, out var apiKey, out error))
            {
                return false;
            }

            credential = new ProviderCredential(apiKey);
            return true;
        }

        if (!TryResolve(provider, out _, out error))
        {
            return false;
        }

        var integration = plugins.FindIntegration(provider.Integration!);
        if (integration is null)
        {
            error = $"Provider {provider.Name.Value} needs integration '{provider.Integration}', which is not registered.";
            return false;
        }

        var invalid = integration.Validate(provider);
        if (invalid is not null)
        {
            error = $"Provider {provider.Name.Value}: {invalid}";
            return false;
        }

        credential = new ProviderCredential(
            string.Empty,
            integration.CreateHandler(provider, Authentication.Open(provider)));
        error = string.Empty;
        return true;
    }

    public bool TryResolve(
        ProviderDefinition provider,
        out string apiKey,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (provider.Type == ProviderType.Authentication)
        {
            apiKey = string.Empty;
            try
            {
                if (Authentication.IsSignedIn(provider))
                {
                    error = string.Empty;
                    return true;
                }
            }
            catch (InvalidOperationException exception)
            {
                // A damaged login file names its path and nothing from inside it.
                error = exception.Message;
                return false;
            }

            error = $"Provider {provider.Name.Value} is not signed in. Run crystal login {provider.Name.Value}.";
            return false;
        }

        var fromEnvironment = ReadEnvironment(provider);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            apiKey = fromEnvironment;
            error = string.Empty;
            return true;
        }

        if (provider.ApiKey is not null)
        {
            return ApiKeyText.TryResolve(provider.ApiKey, _home, out apiKey, out error);
        }

        if (TryReadFile(provider.Name, out var fromFile))
        {
            apiKey = fromFile;
            error = string.Empty;
            return true;
        }

        if (!provider.RequiresApiKey)
        {
            apiKey = string.Empty;
            error = string.Empty;
            return true;
        }

        apiKey = string.Empty;
        var environmentName = ResolveEnvironmentName(provider);
        error =
            $"Missing API key for {provider.Name.Value}. "
            + $"Set {provider.Name.Value}.apiKey in its provider definition "
            + $"(literal, {{env:NAME}}, or {{file:path}}), "
            + $"or set {environmentName} / {SharedApiKeyVariable}, "
            + $"or write {_home.CredentialsPath}.";
        return false;
    }

    public void Save(ProviderName provider, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        _home.EnsureCreated();

        var document = ReadDocument();
        document[provider.Value] = new ProviderCredentialsDocument
        {
            ApiKey = apiKey
        };

        AtomicFile.WriteText(
            _home.CredentialsPath,
            JsonSerializer.Serialize(document, HomeJson.Options),
            ownerOnly: true,
            followLink: true);
    }

    private static string? ReadEnvironment(ProviderDefinition provider)
    {
        foreach (var name in EnvironmentNames(provider))
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static IEnumerable<string> EnvironmentNames(ProviderDefinition provider)
    {
        var primary = ResolveEnvironmentName(provider);
        yield return primary;

        var derived = provider.Name.ApiKeyEnvironmentName;
        if (!string.Equals(derived, primary, StringComparison.Ordinal))
        {
            yield return derived;
        }

        if (provider.RequiresApiKey)
        {
            yield return SharedApiKeyVariable;
        }
    }

    private static string ResolveEnvironmentName(ProviderDefinition provider) =>
        string.IsNullOrWhiteSpace(provider.ApiKeyEnvironment)
            ? provider.Name.ApiKeyEnvironmentName
            : provider.ApiKeyEnvironment;

    private bool TryReadFile(ProviderName provider, out string apiKey)
    {
        apiKey = string.Empty;
        if (!File.Exists(_home.CredentialsPath))
        {
            return false;
        }

        var document = ReadDocument();
        if (!document.TryGetValue(provider.Value, out var entry)
            || string.IsNullOrWhiteSpace(entry.ApiKey))
        {
            return false;
        }

        apiKey = entry.ApiKey.Trim();
        return true;
    }

    private Dictionary<string, ProviderCredentialsDocument> ReadDocument()
    {
        if (!File.Exists(_home.CredentialsPath))
        {
            return new Dictionary<string, ProviderCredentialsDocument>(StringComparer.Ordinal);
        }

        var json = File.ReadAllText(_home.CredentialsPath);
        return JsonSerializer.Deserialize<Dictionary<string, ProviderCredentialsDocument>>(
            json,
            HomeJson.Options)
            ?? new Dictionary<string, ProviderCredentialsDocument>(StringComparer.Ordinal);
    }
}
