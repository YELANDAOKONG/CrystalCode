using CrystalCode.Display.Paint;
using CrystalCode.Display.Shell;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Tools;

namespace CrystalCode.Terminal;

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

    public const string ExtendedReviewing = "Extended Review";

    public const string WaitingForAnswer = "Waiting For Answer";

    public const string Compacting = "Compacting";

    public const string NamingTopic = "Naming Topic";

    public const string LoadingTools = "Loading Tools";

    public const string LoadingPlugins = "Loading Plugins";

    /// <summary>One row of the startup page, or the live caption suffix.</summary>
    public static string Loading(LoadingKind kind, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var label = kind switch
        {
            LoadingKind.ToolSet => "Tool Set",
            LoadingKind.Plugin => "Plugin",
            LoadingKind.McpServer => "MCP Server",
            _ => "Item"
        };
        return label + "  " + name;
    }

    /// <summary>The progress row while one item loads in a live session.</summary>
    public static string LoadingItem(LoadingKind kind, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var caption = kind == LoadingKind.Plugin ? LoadingPlugins : LoadingTools;
        return caption + "  " + name;
    }

    public static string Retrying(int attempt, TimeSpan delay)
    {
        if (attempt <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attempt), attempt, "Attempt must be positive.");
        }

        if (delay < TimeSpan.Zero)
        {
            delay = TimeSpan.Zero;
        }

        return "Retrying In " + ProgressElapsed.Format(delay) + " (Attempt " + attempt + ")";
    }

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
            BackgroundBashStartTool.ToolName => "Starting Background Command",
            BackgroundBashStatusTool.ToolName => "Checking Background Command",
            BackgroundBashLogsTool.ToolName => "Reading Background Logs",
            BackgroundBashStopTool.ToolName => "Stopping Background Command",
            ReadTool.ToolName => "Reading",
            ListTool.ToolName => "Listing",
            TreeTool.ToolName => "Listing",
            WriteTool.ToolName => "Writing File",
            EditTool.ToolName => "Editing",
            ApplyPatchTool.ToolName => "Applying Patch",
            GlobTool.ToolName => "Searching",
            GrepTool.ToolName => "Searching",
            SearchTool.ToolName => "Searching",
            TodoWriteTool.ToolName => "Updating Todos",
            TodoReadTool.ToolName => "Reading Todos",
            QuestionTool.ToolName => "Asking",
            SkillTool.ToolName => "Loading Skill",
            _ => "Running: " + DisplayCase.Token(toolName)
        };
    }
}
