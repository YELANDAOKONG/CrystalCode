using CrystalCode.Engine.Approvals;

namespace CrystalCode.Terminal.Approvals;

/// <summary>
/// Maps approval keys. y/enter once, s session, a always, n/esc deny.
/// </summary>
public static class ApprovalKeys
{
    public static bool TryMap(ConsoleKey key, out ApprovalChoice choice, bool allowReusableGrant = true)
    {
        if (!allowReusableGrant && key is ConsoleKey.D2 or ConsoleKey.S or ConsoleKey.D3 or ConsoleKey.A)
        {
            choice = ApprovalChoice.Deny;
            return false;
        }

        switch (key)
        {
            case ConsoleKey.D1:
            case ConsoleKey.Y:
            case ConsoleKey.Enter:
                choice = ApprovalChoice.AllowOnce;
                return true;
            case ConsoleKey.D2:
            case ConsoleKey.S:
                choice = ApprovalChoice.AllowSession;
                return true;
            case ConsoleKey.D3:
            case ConsoleKey.A:
                choice = ApprovalChoice.AllowPersistent;
                return true;
            case ConsoleKey.D4:
            case ConsoleKey.N:
            case ConsoleKey.Escape:
                choice = ApprovalChoice.Deny;
                return true;
            default:
                choice = ApprovalChoice.Deny;
                return false;
        }
    }
}
