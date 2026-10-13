namespace CrystalCode.Engine.Tools;

public sealed record BackgroundTaskLog(
    string Text,
    int NextOffset,
    bool HasMore,
    bool OffsetExpired);
