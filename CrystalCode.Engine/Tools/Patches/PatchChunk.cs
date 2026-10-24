namespace CrystalCode.Engine.Tools.Patches;

/// <summary>
/// One group of changes inside an Update File section. Every context line
/// must be found, in order, before the old lines are looked for.
/// <paramref name="OldLines"/> and <paramref name="NewLines"/> include the
/// unchanged lines around the edit; <paramref name="RemovedLines"/> and
/// <paramref name="AddedLines"/> hold only the lines that change.
/// </summary>
internal sealed record PatchChunk(
    IReadOnlyList<string> Contexts,
    IReadOnlyList<string> OldLines,
    IReadOnlyList<string> NewLines,
    bool AtEndOfFile,
    IReadOnlyList<string> RemovedLines,
    IReadOnlyList<string> AddedLines);
