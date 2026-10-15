namespace CrystalCode.Engine.Tools;

/// <summary>
/// Size and probe limits for workspace tools.
/// Sized for real repositories, not demo snippets.
/// </summary>
public static class WorkspaceLimits
{
    public const int BinaryProbeBytes = 8192;

    public const int ConfirmPreviewCharacters = 16_000;

    public const int MaximumReadCharacters = 1_000_000;

    public const int MaximumReadLines = 20_000;

    public const int MaximumWriteBytes = 2 * 1024 * 1024;

    public const int MaximumGrepFileBytes = 8 * 1024 * 1024;

    public const int MaximumGrepMatches = 1000;

    public const int MaximumGlobMatches = 1000;

    public const int DefaultSearchFileLimit = 50;

    public const int MaximumSearchFiles = 200;

    public const int MaximumSearchMatchesPerFile = 200;

    public const int MaximumSearchPreviewLinesPerFile = 2;

    public const int MaximumSearchPreviewCharacters = 200;

    public const int MaximumListEntries = 1000;

    public const int DefaultTreeDepth = 2;

    public const int MaximumTreeDepth = 4;

    public const int MaximumTreeEntries = 1000;

    public const int MaximumTreeFilesPerDirectory = 20;

    public const int MaximumToolOutputCharacters = 100_000;

    public const int MaximumBackgroundTaskOutputCharacters = 1_048_576;

    public const int MaximumBackgroundLogCharacters = 100_000;

    public const int MaximumBackgroundTasks = 32;

    public const int BashTimeoutSeconds = 600;

    public const int BackgroundBashTimeoutSeconds = 10800;

    public const int ExternalToolTimeoutSeconds = 120;

    // CancelAfter rejects a delay at or above uint.MaxValue milliseconds.
    public const int MaximumBashTimeoutSeconds = (int)((uint.MaxValue - 1) / 1000);
}
