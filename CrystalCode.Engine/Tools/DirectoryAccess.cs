namespace CrystalCode.Engine.Tools;

/// <summary>
/// One session's directory access rule. Write includes reading.
/// </summary>
public sealed record DirectoryAccess(string Path, string Mode);
