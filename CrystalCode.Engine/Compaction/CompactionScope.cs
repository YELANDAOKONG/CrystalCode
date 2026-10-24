namespace CrystalCode.Engine.Compaction;

/// <summary>
/// How much of the transcript a compaction folds into the summary.
/// </summary>
public enum CompactionScope
{
    /// <summary>
    /// Folds older turns and keeps a recent tail verbatim. Used when the
    /// context nears its limit.
    /// </summary>
    Retained,

    /// <summary>
    /// Like <see cref="Retained"/>, but when the tail already holds every
    /// turn, folds all turns before the latest user turn.
    /// </summary>
    LatestTurn,

    /// <summary>
    /// Folds every turn, including the latest, and keeps nothing verbatim.
    /// </summary>
    Everything
}
