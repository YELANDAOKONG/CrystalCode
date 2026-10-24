namespace CrystalCode.Engine.Tools;

/// <summary>
/// A run of lines in a changed file. <paramref name="FirstLine"/> is 1-based.
/// A deletion has no lines left, so its count is zero and its first line is
/// the line that now sits where the text was.
/// </summary>
internal readonly record struct SnippetRegion(int FirstLine, int LineCount);
