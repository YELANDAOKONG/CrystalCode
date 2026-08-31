namespace CrystalCode.Display.Shell;

/// <summary>
/// One-cell braille frames for the live progress row. Not emoji.
/// </summary>
public static class ProgressSpinner
{
    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(80);

    private static readonly string[] Frames =
    [
        "⠋",
        "⠙",
        "⠹",
        "⠸",
        "⠼",
        "⠴",
        "⠦",
        "⠧",
        "⠇",
        "⠏"
    ];

    public static int FrameCount => Frames.Length;

    public static string Frame(int index)
    {
        var count = Frames.Length;
        var wrapped = index % count;
        if (wrapped < 0)
        {
            wrapped += count;
        }

        return Frames[wrapped];
    }
}
