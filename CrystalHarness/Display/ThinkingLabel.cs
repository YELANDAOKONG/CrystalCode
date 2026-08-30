using CrystalHarness.Configuration;

namespace CrystalHarness.Display;

/// <summary>
/// Capitalized thinking-gear label for chrome and notes.
/// </summary>
public static class ThinkingLabel
{
    public static string For(ThinkingSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return DisplayCase.Token(selection.Value);
    }
}
