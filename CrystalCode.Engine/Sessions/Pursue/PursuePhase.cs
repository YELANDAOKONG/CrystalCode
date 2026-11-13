namespace CrystalCode.Engine.Sessions.Pursue;

/// <summary>What an active pursuit is doing right now. None unless it is active.</summary>
public enum PursuePhase
{
    None,
    Working,
    Checking,
    Waiting
}
