namespace CrystalCode.Engine.Tools.Patches;

/// <summary>
/// Creates a file from the listed lines.
/// </summary>
internal sealed record PatchAddition(string Path, IReadOnlyList<string> Lines) : PatchOperation(Path);
