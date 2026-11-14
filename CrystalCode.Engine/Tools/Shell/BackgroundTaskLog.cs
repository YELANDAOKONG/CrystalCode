namespace CrystalCode.Engine.Tools.Shell;

public sealed record BackgroundTaskLog(
    string Text,
    int NextOffset,
    bool HasMore,
    bool OffsetExpired);
