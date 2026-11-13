namespace CrystalCode.Engine.Sessions.Pursue;

/// <summary>The exact rendered entry a checker cited, independent of later compaction.</summary>
public sealed record PursueEvidence(int Number, string Kind, string Text);
