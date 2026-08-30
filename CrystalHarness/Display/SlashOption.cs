namespace CrystalHarness.Display;

/// <summary>
/// One completable slash command. Keys are names without the leading slash.
/// </summary>
public sealed record SlashOption(
    string Name,
    string Help,
    IReadOnlyList<string> Keys,
    IReadOnlyList<SlashOption>? Arguments = null)
{
    public IReadOnlyList<SlashOption> ArgumentOptions => Arguments ?? [];
}
