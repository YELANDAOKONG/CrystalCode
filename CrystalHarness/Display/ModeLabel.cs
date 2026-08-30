namespace CrystalHarness.Display;

/// <summary>
/// Capitalized Plan / Work labels for chrome and the composer.
/// </summary>
public static class ModeLabel
{
    public static string For(bool planMode) => planMode ? "Plan" : "Work";
}
