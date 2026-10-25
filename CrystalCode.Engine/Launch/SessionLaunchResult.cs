namespace CrystalCode.Engine.Launch;

/// <summary>
/// A prepared launch, or the reason there is none.
/// </summary>
public sealed record SessionLaunchResult
{
    private SessionLaunchResult(SessionLaunch? launch, SessionLaunchFailureKind? failure, string error)
    {
        Launch = launch;
        Failure = failure;
        Error = error;
    }

    public SessionLaunch? Launch { get; }

    public SessionLaunchFailureKind? Failure { get; }

    /// <summary>English text for the failure, or empty after a success.</summary>
    public string Error { get; }

    public static SessionLaunchResult Ready(SessionLaunch launch)
    {
        ArgumentNullException.ThrowIfNull(launch);
        return new SessionLaunchResult(launch, null, string.Empty);
    }

    public static SessionLaunchResult Failed(SessionLaunchFailureKind kind, string error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new SessionLaunchResult(null, kind, error);
    }
}
