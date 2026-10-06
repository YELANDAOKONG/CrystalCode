using CrystalCode.Engine.Sessions;

namespace CrystalCode.Engine.Tests.Sessions;

internal sealed class ScriptedTrustPrompt : IWorkspaceTrustPrompt
{
    private readonly bool _accept;

    public ScriptedTrustPrompt(bool accept)
    {
        _accept = accept;
    }

    public int Asked { get; private set; }

    public WorkspaceTrustRequest? Last { get; private set; }

    public ValueTask<bool> ConfirmAsync(
        WorkspaceTrustRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        Asked++;
        Last = request;
        return ValueTask.FromResult(_accept);
    }
}
