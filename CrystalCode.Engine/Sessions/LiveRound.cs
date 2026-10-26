using Crystal.Tools;

namespace CrystalCode.Engine.Sessions;

/// <summary>
/// The model round that is still streaming, assembled from its deltas.
/// Opaque reasoning state is never included.
/// </summary>
/// <param name="Text">Assistant text so far.</param>
/// <param name="Reasoning">Readable reasoning so far.</param>
/// <param name="ToolCalls">Tool calls so far; arguments may be incomplete.</param>
/// <param name="Retries">Retries scheduled for this round.</param>
public sealed record LiveRound(
    string Text,
    string Reasoning,
    IReadOnlyList<ToolCall> ToolCalls,
    int Retries);
