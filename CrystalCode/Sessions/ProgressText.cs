using CrystalCode.Display.Paint;
using CrystalCode.Tools;

namespace CrystalCode.Sessions;

/// <summary>
/// Live work captions for the progress row above the status bar.
/// Independent of the status-bar activity bullet.
/// </summary>
public static class ProgressText
{
    public const string WaitingForModel = "Waiting For Model";

    public const string Thinking = "Thinking";

    public const string Writing = "Writing";

    public const string AwaitingApproval = "Awaiting Approval";

    public const string Reviewing = "Reviewing";

    public const string WaitingForAnswer = "Waiting For Answer";

    public const string Compacting = "Compacting";

    public static string Calling(string toolName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        return "Calling " + DisplayCase.Token(toolName);
    }

    public static string Running(string toolName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        return toolName.ToLowerInvariant() switch
        {
            BashTool.ToolName => "Running Command",
            ReadTool.ToolName => "Reading",
            WriteTool.ToolName => "Writing File",
            EditTool.ToolName => "Editing",
            GlobTool.ToolName => "Searching",
            GrepTool.ToolName => "Searching",
            TodoWriteTool.ToolName => "Updating Todos",
            QuestionTool.ToolName => "Asking",
            SkillTool.ToolName => "Loading Skill",
            _ => "Running " + DisplayCase.Token(toolName)
        };
    }
}
