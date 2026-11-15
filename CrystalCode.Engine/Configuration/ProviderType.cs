namespace CrystalCode.Engine.Configuration;

/// <summary>
/// How a provider entry gets the credential its requests carry.
/// </summary>
public enum ProviderType
{
    /// <summary>
    /// A static API key from the environment, the entry, or credentials.json.
    /// </summary>
    Key,

    /// <summary>
    /// An account login owned by a registered authentication integration.
    /// </summary>
    Authentication
}
