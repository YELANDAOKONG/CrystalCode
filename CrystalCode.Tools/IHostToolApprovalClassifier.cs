using Crystal.Tools;

namespace CrystalCode.Tools;

/// <summary>
/// Classifies one external tool call before the host invokes the tool, for
/// tools whose paths are embedded in structured arguments the host cannot
/// see. The host merges the result with its own classification: a tool can
/// raise risk or authority for the call, never lower them, and a failure
/// fails closed.
/// </summary>
public interface IHostToolApprovalClassifier
{
    /// <summary>
    /// Returns the risk and authority proposed for one model-generated call.
    /// Must not perform side effects.
    /// </summary>
    /// <param name="call">The exact model-generated tool call.</param>
    /// <param name="context">
    /// Host facts for the call. The host supplies the live workspace root,
    /// session id, and approval mode; the data directories are empty.
    /// </param>
    HostToolApprovalClassification ClassifyApproval(
        ToolCall call,
        ToolHostContext context);
}
