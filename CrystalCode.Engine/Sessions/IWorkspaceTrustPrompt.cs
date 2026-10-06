namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Asks whether the operator trusts a directory. The engine does not draw
/// this prompt. A front end that cannot ask returns false.
/// </summary>
public interface IWorkspaceTrustPrompt
{
    ValueTask<bool> ConfirmAsync(
        WorkspaceTrustRequest request,
        CancellationToken cancellationToken);
}
