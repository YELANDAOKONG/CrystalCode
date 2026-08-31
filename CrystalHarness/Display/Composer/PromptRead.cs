namespace CrystalHarness.Display.Composer;

/// <summary>
/// One composer result: submitted text, or the in-flight turn ended.
/// </summary>
public sealed record PromptRead(string Text, bool TurnEnded)
{
    public static PromptRead Submitted(string text) => new(text, false);

    public static PromptRead Ended { get; } = new(string.Empty, true);
}
