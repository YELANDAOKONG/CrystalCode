namespace CrystalCode.Engine.Tools;

/// <summary>
/// What one file would look like after a proposed <c>edit</c>, <c>write</c>,
/// or <c>apply_patch</c> call, for a front end that shows a diff before the
/// call runs.
/// </summary>
/// <param name="Path">The absolute path the call changes.</param>
/// <param name="OldText">The current text, or null when the file does not exist yet.</param>
/// <param name="NewText">The text after the change; empty for a delete.</param>
/// <param name="Delete">True when the call removes the file.</param>
/// <param name="MoveFrom">The source path when the change is a move, otherwise null.</param>
public sealed record ToolFileChange(
    string Path,
    string? OldText,
    string NewText,
    bool Delete = false,
    string? MoveFrom = null);
