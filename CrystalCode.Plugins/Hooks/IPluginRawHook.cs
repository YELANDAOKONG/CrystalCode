namespace CrystalCode.Plugins.Hooks;

/// <summary>
/// Privileged extension points. A plugin registers raw hooks separately from
/// <see cref="IPluginHook"/>, on <see cref="PluginContribution.RawHooks"/>.
/// The host does not hold a raw hook to the safety rules that bind ordinary
/// hooks. It refuses only what it cannot represent. Raw hooks run in plugin
/// load order and each one sees the previous replacement. A raw hook that
/// throws is skipped.
/// </summary>
public interface IPluginRawHook
{
    /// <summary>
    /// Returns the item list for this model call, or null to keep the current
    /// list. The list may drop, reorder, add, or rewrite items of any kind,
    /// including the live system prompt, and may split a tool call from its
    /// result. The stored transcript and the archive are not changed. The
    /// provider may reject a request it does not accept, and the host does
    /// not check that in advance.
    /// <para>
    /// The host skips a replacement that repeats an item id, returns empty
    /// reasoning text, or names an image that is not attached to the session.
    /// It also skips a replacement that adds an image when
    /// <see cref="PluginModelRequest.AcceptsImages"/> is false. A raw hook
    /// cannot create an image. It can keep, drop, or move images that are
    /// already attached. Only user messages and tool results carry images to
    /// the model. A new item needs an id the request does not already use. A
    /// new tool result ignores its name, and new reasoning carries no
    /// provider state.
    /// </para>
    /// </summary>
    ValueTask<IReadOnlyList<PluginModelItem>?> RebuildModelAsync(
        PluginModelRequest request,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<PluginModelItem>?>(null);
}
