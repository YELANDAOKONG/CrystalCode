using CrystalCode.Display.Paint;
using CrystalCode.Engine.Tools;

namespace CrystalCode.Terminal;

/// <summary>
/// One-line tool call for the transcript. Never dumps streaming JSON fragments.
/// </summary>
public static class ToolCallText
{
    /// <summary>
    /// Model-supplied names, paths, and commands are shown with hidden
    /// characters spelled out, so an escape sequence or a bidirectional
    /// override cannot alter what an operator reads in an approval card.
    /// </summary>
    public static string Summary(string name, string arguments) =>
        TerminalText.Reveal(ToolCallSummary.Text(name, arguments));
}
