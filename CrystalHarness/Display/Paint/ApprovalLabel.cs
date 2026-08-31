using CrystalHarness.Approvals;

namespace CrystalHarness.Display.Paint;

/// <summary>
/// Capitalized approval-mode label for chrome.
/// </summary>
public static class ApprovalLabel
{
    public static string For(ApprovalMode mode)
    {
        ArgumentNullException.ThrowIfNull(mode);
        return DisplayCase.Token(mode.Value);
    }
}
