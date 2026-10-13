namespace CrystalCode.Engine.Tools;

public sealed record BackgroundTaskSnapshot(
    string TaskId,
    string Command,
    string WorkingDirectory,
    BackgroundTaskStatus Status,
    int? ExitCode,
    DateTimeOffset StartedUtc,
    DateTimeOffset? FinishedUtc,
    int LogStartOffset,
    int LogEndOffset);
