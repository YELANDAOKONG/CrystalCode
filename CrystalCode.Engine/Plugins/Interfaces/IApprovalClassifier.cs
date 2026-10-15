using Crystal.Tools;
using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Tools;

namespace CrystalCode.Engine.Plugins.Interfaces;

/// <summary>
/// Classifies a tool the built-in switch does not know.
/// </summary>
public interface IApprovalClassifier
{
    bool TryClassify(
        ToolCall call,
        Workspace workspace,
        out ToolClassification classification);

    /// <summary>
    /// Returns null when this classifier does not own the tool. Asynchronous
    /// classifiers override this method; existing synchronous classifiers keep
    /// their dispatch behavior through the default implementation.
    /// </summary>
    ValueTask<ToolClassification?> ClassifyAsync(
        ToolCall call,
        Workspace workspace,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<ToolClassification?>(
            TryClassify(call, workspace, out var classification) ? classification : null);
    }
}
