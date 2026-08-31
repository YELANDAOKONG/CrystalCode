using System.Text.Json;
using CrystalCode.Configuration;

namespace CrystalCode.Home;

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
    }

    public bool TryResolve(
        ProviderDefinition provider,
        out string apiKey,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(provider);

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

        apiKey = string.Empty;
        var environmentName = ResolveEnvironmentName(provider);
        error =
            $"Missing API key for {provider.Name.Value}. "
            + $"Set providers.{provider.Name.Value}.apiKey in config.json "
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

        var json = JsonSerializer.Serialize(document, HomeJson.Options);
        File.WriteAllText(_home.CredentialsPath, json);
        RestrictOwnerAccess(_home.CredentialsPath);
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

        yield return SharedApiKeyVariable;
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

    private static void RestrictOwnerAccess(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
