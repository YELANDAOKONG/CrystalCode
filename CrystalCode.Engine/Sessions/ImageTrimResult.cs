namespace CrystalCode.Engine.Sessions;

/// <summary>Images newly left out of model requests by one trim.</summary>
public sealed record ImageTrimResult(int Images, long Bytes);
