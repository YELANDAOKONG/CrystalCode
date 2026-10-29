namespace CrystalCode.Engine.Sessions;

/// <summary>
/// One block of a <see cref="TranscriptDocument"/>. <see cref="Kind"/> is
/// <c>paragraph</c>, <c>keyValues</c>, <c>table</c>, or <c>list</c>.
/// Paragraph and list use <see cref="Lines"/>. KeyValues uses two-cell rows.
/// Table uses <see cref="Columns"/> and rows of that width.
/// </summary>
public sealed record TranscriptDocumentBlock(
    string Kind,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    IReadOnlyList<string> Lines,
    int Omitted);
