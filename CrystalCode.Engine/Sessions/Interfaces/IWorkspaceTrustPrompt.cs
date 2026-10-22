namespace CrystalCode.Engine.Sessions.Interfaces;
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
