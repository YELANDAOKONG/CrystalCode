namespace CrystalCode.Tools;

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

    public const int MaximumGrepMatches = 500;

    public const int MaximumGlobMatches = 1000;

    public const int MaximumToolOutputCharacters = 100_000;

    public const int BashTimeoutSeconds = 120;
}
