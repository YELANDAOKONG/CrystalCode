using Crystal.Tools;

using CrystalHarness.Approvals;

namespace CrystalHarness.Tests.Approvals;

internal sealed class RecordingApprovalPrompt : IApprovalPrompt
{
    private readonly ApprovalChoice _choice;

    public RecordingApprovalPrompt(ApprovalChoice choice)
    {
        _choice = choice;
    }

    public int Count { get; private set; }

    public ValueTask<ApprovalChoice> AskAsync(
        ToolCall call,
        ToolClassification classification,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Count++;
        LastClassification = classification;
        return ValueTask.FromResult(_choice);
    }

    public ToolClassification? LastClassification { get; private set; }
}
