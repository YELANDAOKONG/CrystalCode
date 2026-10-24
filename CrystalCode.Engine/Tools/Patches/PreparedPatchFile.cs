namespace CrystalCode.Engine.Tools.Patches;

/// <summary>
/// One file section after it passed every check and before anything is
/// written. <see cref="TargetPath"/> is set only for a move.
/// </summary>
internal sealed record PreparedPatchFile(
    PatchAction Action,
    string SourcePath,
    string SourceShown,
    string? TargetPath,
    string? TargetShown,
    byte[]? OriginalBytes,
    string? NewText,
    IReadOnlyList<SnippetRegion> Regions)
{
    public bool IsMove => TargetPath is not null;

    public string ShownPath => TargetShown ?? SourceShown;

    /// <summary>Path that holds the result after the file is written.</summary>
    public string ResultPath => TargetPath ?? SourcePath;
}
