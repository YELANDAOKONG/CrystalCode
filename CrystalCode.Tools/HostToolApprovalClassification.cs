namespace CrystalCode.Tools;

/// <summary>
/// Approval classification proposed by an external host tool before
/// invocation. The host merges it with its own classification, so it can
/// raise risk or authority for the call, never lower them.
/// </summary>
/// <param name="Risk">
/// Case-insensitive. One of <c>read</c>, <c>write</c>, <c>privileged</c>, or
/// <c>forbidden</c>. An unknown value fails closed as <c>privileged</c>.
/// </param>
/// <param name="Authority">
/// Case-insensitive. One of <c>workspace</c>, <c>outsideWorkspace</c> (or
/// <c>outside_workspace</c>), <c>network</c>, or <c>privilegedEscalation</c>
/// (or <c>privileged_escalation</c>). An unknown value fails closed as
/// <c>privilegedEscalation</c>.
/// </param>
/// <param name="Summary">Plain-English text for the approval prompt.</param>
/// <param name="RequirePrompt">
/// Whether the call must always ask. The host keeps this even when risk and
/// authority are unchanged.
/// </param>
public sealed record HostToolApprovalClassification(
    string Risk,
    string Authority,
    string Summary,
    bool RequirePrompt = false);
