using Crystal.Tools;

namespace CrystalCode.Plugins.Hooks;

/// <summary>
/// Ordered extension points. Later plugins see earlier plugins' replacements.
/// The same pipeline wraps built-in tools, other plugins, and external tools.
/// A hook that throws is skipped.
/// </summary>
public interface IPluginHook
{
    /// <summary>Called when a session becomes active. The context is read-only.</summary>
    ValueTask OnSessionStartedAsync(
        PluginSession session,
        CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    /// <summary>Called when a session ends or the workspace changes.</summary>
    ValueTask OnSessionEndedAsync(
        PluginSession session,
        CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    /// <summary>Returns text to append to the instruction block, or null.</summary>
    string? AppendPrompt(PluginPrompt prompt) => null;

    /// <summary>
    /// Returns a replacement call, or null to keep the current call.
    /// The call id must stay the same. The host classifies and approves the
    /// replacement before the tool runs.
    /// </summary>
    ValueTask<ToolCall?> BeforeToolAsync(
        ToolCall call,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<ToolCall?>(null);

    /// <summary>Returns a replacement result, or null to keep the current result.</summary>
    ValueTask<PluginToolResult?> AfterToolAsync(
        ToolCall call,
        PluginToolResult result,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<PluginToolResult?>(null);

    /// <summary>Returns stricter approval advice, or null to keep the host classification.</summary>
    PluginApprovalAdvice? AdviseApproval(ToolCall call, PluginApprovalFacts facts) => null;

    /// <summary>Returns text to append to the compaction prompt or summary, or null.</summary>
    string? AppendCompaction(PluginCompaction compaction) => null;
}
