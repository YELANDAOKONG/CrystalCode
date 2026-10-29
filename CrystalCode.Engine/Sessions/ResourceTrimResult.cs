namespace CrystalCode.Engine.Sessions;

/// <summary>Resources newly left out of model requests by one trim.</summary>
public sealed record ResourceTrimResult(int Resources, long Bytes);
