namespace CrystalCode.Engine.Sessions.Pursue;

/// <summary>Stable tokens for why a pursuit is not active.</summary>
public static class PursueReasonCodes
{
    public const string Done = "done";

    public const string UserPaused = "user_paused";

    public const string Interrupted = "interrupted";

    public const string InputPending = "input_pending";

    public const string Restarted = "restarted";

    public const string ModelLimit = "model_limit";

    public const string ToolLimit = "tool_limit";

    public const string DurationLimit = "duration_limit";

    public const string ContinuationLimit = "continuation_limit";

    public const string Stalled = "stalled";

    public const string WaitTimeout = "wait_timeout";

    public const string ReviewFailed = "review_failed";

    public const string ReviewInvalid = "review_invalid";

    public const string ContextOverflow = "context_overflow";

    public const string RunFailed = "run_failed";

    public const string Blocked = "blocked";
}
