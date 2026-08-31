namespace CrystalCode.Sessions;

/// <summary>
/// One built-in slash verb and the names that map to it.
/// </summary>
public sealed record SlashSpec(
    SessionVerb Verb,
    string Name,
    IReadOnlyList<string> Aliases,
    string Help,
    IReadOnlyList<string>? Arguments = null);
