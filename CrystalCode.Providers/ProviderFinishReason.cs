using Crystal;

namespace CrystalCode.Providers;

/// <summary>
/// Finish reasons that originate in a provider rather than in Crystal's
/// conventional set.
/// </summary>
public static class ProviderFinishReason
{
    /// <summary>
    /// Gets the finish reason for a candidate that ended because the provider
    /// paused a server-side tool loop. The round is unfinished: sending its
    /// content back as the last assistant turn lets the provider resume.
    /// </summary>
    public static FinishReason PauseTurn { get; } = new("pause_turn");
}
