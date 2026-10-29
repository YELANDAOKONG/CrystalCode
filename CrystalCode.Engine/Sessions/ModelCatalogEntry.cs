namespace CrystalCode.Engine.Sessions;

/// <summary>
/// One selectable model and the choices it declares. It carries no base
/// address, header, or credential, so it is safe to send to a front end.
/// <see cref="CredentialReady"/> is false when selecting it would be refused
/// for a missing API key.
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
    int? MaxTokens);
