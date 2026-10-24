namespace CrystalCode.Engine.Tools.Patches;

/// <summary>
/// Removes an existing file.
/// </summary>
internal sealed record PatchDeletion(string Path) : PatchOperation(Path);
