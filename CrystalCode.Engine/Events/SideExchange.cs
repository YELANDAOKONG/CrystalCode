namespace CrystalCode.Engine.Events;

/// <summary>
/// One side question and its answer. Kept in memory for this process only.
/// </summary>
public sealed record SideExchange(string Question, string Answer);
