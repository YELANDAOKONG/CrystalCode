namespace CrystalCode.Engine.Sessions;

/// <summary>
/// One tool's call count inside a stats report.
/// </summary>
public sealed record SessionToolCount(string Name, int Count);
