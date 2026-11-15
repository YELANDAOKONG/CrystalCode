namespace CrystalCode.Engine.Tools;

internal sealed record MultiEditPreparation(
    bool ContinueOnError,
    IReadOnlyList<PreparedMultiEdit> Files,
    IReadOnlyList<string> Failures);
