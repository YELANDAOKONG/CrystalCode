namespace CrystalCode.Engine.Sessions;

/// <summary>
/// One completable slash command or argument. Keys are names without the
/// leading slash. Front ends project this onto their own command surface.
/// </summary>
public sealed record SlashCompletion(
    string Name,
    string Help,
    IReadOnlyList<string> Keys,
    IReadOnlyList<SlashCompletion>? Arguments = null,
    bool ArgumentsOptional = false,
    IReadOnlyList<SlashCompletion>? ArgumentsAfterValue = null)
{
    public IReadOnlyList<SlashCompletion> ArgumentOptions => Arguments ?? [];

    public IReadOnlyList<SlashCompletion> TrailingArgumentOptions => ArgumentsAfterValue ?? [];
}
