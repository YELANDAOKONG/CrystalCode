namespace CrystalCode.Engine.Tools;

internal sealed record MultiEditFile(string Path, IReadOnlyList<TextReplacement> Edits);
