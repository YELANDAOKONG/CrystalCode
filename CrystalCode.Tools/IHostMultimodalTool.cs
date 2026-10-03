using Crystal.Multimodal.Tools;

namespace CrystalCode.Tools;

/// <summary>
/// A multimodal tool that receives <see cref="ToolHostContext"/> with each call.
/// A type that implements only <see cref="IMultimodalTool"/> is unchanged.
/// </summary>
public interface IHostMultimodalTool : IMultimodalTool
{
    /// <summary>
    /// Invokes the tool with one model call and the host facts for that call.
    /// </summary>
    /// <param name="call">The exact model-generated tool call.</param>
    /// <param name="context">Host facts captured for this call.</param>
    /// <param name="cancellationToken">A token that cancels the tool operation.</param>
    /// <returns>The exact caller-owned multimodal output.</returns>
    ValueTask<MultimodalToolOutput> InvokeAsync(
        MultimodalToolCall call,
        ToolHostContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fails. The host calls the overload that accepts <see cref="ToolHostContext"/>.
    /// </summary>
    ValueTask<MultimodalToolOutput> IMultimodalTool.InvokeAsync(
        MultimodalToolCall call,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("This tool requires host context.");
}
