namespace CrystalCode.Engine.Events;

/// <summary>
/// Engine-originated work that is not part of a streaming model round.
/// </summary>
public enum SessionActivity
{
    Idle,
    LoadingTools,
    LoadingPlugins,
    Compacting,
    WaitingForModel
}
