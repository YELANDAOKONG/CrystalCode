namespace CrystalCode.Engine.Tools;

/// <summary>
/// What one file section of an <c>apply_patch</c> call would do, for an
/// approval card. Only the lines that change are listed.
/// </summary>
/// <param name="Action"><c>add</c>, <c>update</c>, or <c>delete</c>.</param>
/// <param name="Path">The path as written in the patch.</param>
/// <param name="MovePath">The destination of a move, or null.</param>
/// <param name="RemovedLines">Lines the section removes.</param>
/// <param name="AddedLines">Lines the section adds.</param>
public sealed record PatchFilePreview(
    string Action,
    string Path,
    string? MovePath,
    IReadOnlyList<string> RemovedLines,
    IReadOnlyList<string> AddedLines);
