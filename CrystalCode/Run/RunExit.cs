using CrystalCode.Engine.Sessions;

namespace CrystalCode.Run;

/// <summary>
/// Process status for <c>crystal run</c>. A failure or a budget stop outranks
/// an operator denial. An interrupt outranks a denial as well.
/// </summary>
internal static class RunExit
{
    public const int Completed = 0;

    public const int Invalid = 1;

    public const int Failed = 2;

    public const int Limited = 3;

    public const int Denied = 4;

    public const int Interrupted = 5;

    public static (int Code, string? Status) From(TurnStopReason reason, bool operatorDenied)
    {
        ArgumentNullException.ThrowIfNull(reason);
        if (reason == TurnStopReason.Failed)
        {
            return (Failed, "failed");
        }

        if (reason == TurnStopReason.ModelCallLimitReached
            || reason == TurnStopReason.ToolCallLimitReached
            || reason == TurnStopReason.DurationLimitReached
            || reason == TurnStopReason.ContextOverflow)
        {
            return (Limited, reason.Value);
        }

        if (reason == TurnStopReason.Interrupted)
        {
            return (Interrupted, "interrupted");
        }

        if (operatorDenied)
        {
            return (Denied, "operator prompt denied");
        }

        if (reason == TurnStopReason.Completed)
        {
            return (Completed, null);
        }

        return (Failed, reason.Value);
    }
}
