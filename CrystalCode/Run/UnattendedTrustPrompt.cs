using CrystalCode.Engine.Sessions;

namespace CrystalCode.Run;

/// <summary>
/// Refuses a trust question. <c>crystal run</c> decides trust before the
/// session exists, so this prompt is only a fail-closed fallback.
/// </summary>
internal sealed class UnattendedTrustPrompt : IWorkspaceTrustPrompt
{
    public ValueTask<bool> ConfirmAsync(
        WorkspaceTrustRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(false);
    }
}
