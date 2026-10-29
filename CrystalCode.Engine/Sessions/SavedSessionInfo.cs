namespace CrystalCode.Engine.Sessions;

/// <summary>
/// One saved session a front end can list. <see cref="Open"/> is true when a
/// process, possibly this one, holds the session; <see cref="Current"/> is
/// true for the session this process has open. <see cref="Topic"/> is the
/// session topic, or null for one without a name.
/// </summary>
public sealed record SavedSessionInfo(
    string Id,
    string Workspace,
    string Mode,
    DateTimeOffset? CreatedUtc,
    DateTimeOffset? UpdatedUtc,
    int UserTurns,
    string Preview,
    bool Current,
    bool Open,
    string? Topic = null);
