using Crystal.Tools;

using CrystalHarness.Approvals;

namespace CrystalHarness.Tests.Approvals;

internal sealed class ThrowingApprovalPrompt : IApprovalPrompt
{
    public ValueTask<ApprovalChoice> AskAsync(
        ToolCall call,
        ToolClassification classification,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Approval prompt should not be called.");
}
