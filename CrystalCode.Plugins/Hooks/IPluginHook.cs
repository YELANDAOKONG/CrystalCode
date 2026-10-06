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
    string? OnPrompt(PluginPrompt prompt) => null;

    /// <summary>
    /// Returns replacement user text, or null to keep the current text.
    /// Whitespace is ignored. The stored message is what remains.
    /// </summary>
    ValueTask<string?> OnUserMessageAsync(
        PluginUserMessage message,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<string?>(null);

    /// <summary>Called when a user turn starts. The turn is read-only.</summary>
    ValueTask OnTurnStartedAsync(
        PluginTurn turn,
        CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    /// <summary>Called when a user turn ends. The turn is read-only.</summary>
    ValueTask OnTurnFinishedAsync(
        PluginTurn turn,
        CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    /// <summary>
    /// Returns a reordered or reduced item list for this model call, or null
    /// to keep the current list. The host rejects a replacement that changes
    /// the live system prompt, adds a tool call, or splits a call from its result.
    /// </summary>
    ValueTask<IReadOnlyList<PluginModelItem>?> RebuildModelAsync(
        PluginModelRequest request,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<PluginModelItem>?>(null);

    /// <summary>
    /// Returns the same items with revised text or fewer images, or null to
    /// keep the current list. The host rejects a replacement that reorders
    /// items, changes roles, or edits the system prompt.
    /// </summary>
    ValueTask<IReadOnlyList<PluginModelItem>?> TransformModelAsync(
        PluginModelRequest request,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<PluginModelItem>?>(null);

    /// <summary>
    /// Returns a replacement call, or null to keep the current call.
    /// The call id must stay the same. The host classifies and approves the
    /// replacement before the tool runs.
    /// </summary>
    ValueTask<ToolCall?> OnToolCallAsync(
        ToolCall call,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<ToolCall?>(null);

    /// <summary>Returns stricter approval advice, or null to keep the host classification.</summary>
    PluginApprovalAdvice? OnApproval(ToolCall call, PluginApprovalFacts facts) => null;

    /// <summary>Returns a replacement result, or null to keep the current result.</summary>
    ValueTask<PluginToolResult?> OnToolResultAsync(
        ToolCall call,
        PluginToolResult result,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<PluginToolResult?>(null);

    /// <summary>Returns text to append to the compaction prompt or summary, or null.</summary>
    string? OnCompaction(PluginCompaction compaction) => null;
}
