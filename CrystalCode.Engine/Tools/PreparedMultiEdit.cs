namespace CrystalCode.Engine.Tools;

internal sealed record PreparedMultiEdit(
    ToolFileChange Change,
    byte[] OriginalBytes,
    bool HasByteOrderMark,
    long ReplacementCount);
