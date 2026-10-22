using Crystal.Tools;

using CrystalCode.Tools;
using CrystalCode.Tools.Interfaces;

namespace CrystalCode.Engine.Tools.External;

/// <summary>
/// Bounds the wait even when external code blocks before returning a task.
/// Abandoned calls remain observed and their instance is not called again.
/// </summary>
internal sealed class HostApprovalClassifierRunner
{
    private const int Ready = 0;
    private const int Running = 1;
    private const int Disabled = 2;
    private static readonly TimeSpan ClassificationTimeout = TimeSpan.FromSeconds(2);

    private readonly IHostToolApprovalClassifier _classifier;
    private int _state;

    public HostApprovalClassifierRunner(IHostToolApprovalClassifier classifier)
    {
        ArgumentNullException.ThrowIfNull(classifier);
        _classifier = classifier;
    }

    public async ValueTask<HostToolApprovalClassification?> ClassifyAsync(
        ToolCall call,
        ToolHostContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _state, Running, Ready) != Ready)
        {
            return null;
        }

        var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ClassificationTimeout);
        var token = timeout.Token;
        // The worker owns the source until external code finishes. It also
        // observes exceptions after the caller has stopped waiting.
        var task = Task.Run(async () =>
        {
            using (timeout)
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    return await _classifier.ClassifyApprovalAsync(call, context, token);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }, CancellationToken.None);

        try
        {
            var result = await task.WaitAsync(token);
            token.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException)
        {
            if (token.IsCancellationRequested)
            {
                Interlocked.Exchange(ref _state, Disabled);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
        finally
        {
            Interlocked.CompareExchange(ref _state, Ready, Running);
        }
    }
}
