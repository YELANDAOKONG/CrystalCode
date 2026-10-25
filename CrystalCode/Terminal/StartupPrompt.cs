using CrystalCode.Engine.Home;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Sessions.Interfaces;

namespace CrystalCode.Terminal;

/// <summary>
/// Trust and resume prompts shown before a session exists. Each question
/// opens its own alternate screen and closes it again. Ctrl+C or Escape
/// declines instead of ending the process.
/// </summary>
internal sealed class StartupPrompt : IWorkspaceTrustPrompt, ISessionChooser
{
    public async ValueTask<bool> ConfirmAsync(
        WorkspaceTrustRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var interrupt = TerminalInterrupt.Link(cancellationToken);
        using var renderer = new SessionRenderer();
        try
        {
            using (renderer.Open())
            {
                return await new TrustPrompt(renderer).ConfirmAsync(request, interrupt.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    public async Task<string?> ChooseAsync(
        IReadOnlyList<SessionSummary> sessions,
        string? currentId,
        bool listWorkspace,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        using var interrupt = TerminalInterrupt.Link(cancellationToken);
        using var renderer = new SessionRenderer();
        try
        {
            using (renderer.Open())
            {
                return await new SessionPicker(renderer).ChooseAsync(
                    sessions,
                    currentId,
                    listWorkspace,
                    interrupt.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}
