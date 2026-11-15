namespace CrystalCode.Engine.Sessions.Catalogs;

/// <summary>
/// One selectable model and the choices it declares. It carries no base
/// address, header, or credential, so it is safe to send to a front end.
/// <see cref="CredentialReady"/> is false when selecting it would be refused
/// for a missing API key or, for an account provider, a missing login.
/// <see cref="Type"/> is <c>default</c> for an API key provider and
/// <c>authentication</c> for an account provider, so a front end can offer
/// sign-in only where it applies.
/// </summary>
public sealed record ModelCatalogEntry(
    string Provider,
    string Model,
    string Protocol,
    bool Current,
    bool CredentialReady,
    IReadOnlyList<int> ContextWindows,
    bool Thinking,
    bool ThinkingCanDisable,
    IReadOnlyList<string> ThinkingEfforts,
    IReadOnlyList<string> ServiceTiers,
    IReadOnlyList<string> Speeds,
    bool ImageInput,
    int? MaxTokens,
    string Type = "default");
