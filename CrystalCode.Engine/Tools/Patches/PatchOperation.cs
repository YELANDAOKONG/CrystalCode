namespace CrystalCode.Engine.Tools.Patches;

/// <summary>
/// One file section of a patch.
/// </summary>
internal abstract record PatchOperation(string Path)
{
    /// <summary>
    /// Every path the operation can write, delete, or create.
    /// </summary>
    public virtual IEnumerable<string> Paths => [Path];
}
