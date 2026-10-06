using Crystal.Tools;

namespace CrystalCode.Plugins.Approvals;

/// <summary>
/// Classifies a tool the built-in switch does not know.
/// Built-in tool names are classified by the host before this runs.
/// </summary>
public interface IPluginClassifier
{
    /// <summary>
    /// Assigns a classification, or returns false to leave the call unknown.
    /// </summary>
    bool TryClassify(
        ToolCall call,
        string workspaceRoot,
        out PluginClassification? classification);
}
