namespace CrystalCode.Engine.Tools;

/// <summary>
/// One entry yielded by the workspace tree walk. For a directory, Path is the
/// resolved directory that is walked, so two directory links to one target
/// report it once. For a file, Path is the directory entry as enumerated, so
/// two file links to one target stay distinct.
/// </summary>
internal readonly record struct TreeEntry(string Path, bool IsDirectory);
