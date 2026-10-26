using CrystalCode.Engine.Home;
using CrystalCode.Engine.Sessions.Interfaces;
using CrystalCode.Server.Interactions;

namespace CrystalCode.Server.Hosting;

/// <summary>
/// Lets the launch ask for trust and a session before the session exists,
/// and moves the server to <see cref="ServerState.AwaitingOperator"/> while
/// it waits.
/// </summary>
internal sealed class StartupPrompts(InteractionBroker broker, Action<string> setState)
    : IWorkspaceTrustPrompt, ISessionChooser
{
    public async ValueTask<bool> ConfirmAsync(
        Engine.Sessions.WorkspaceTrustRequest request,
        CancellationToken cancellationToken)
    {
        setState(ServerState.AwaitingOperator);
        try
        {
            return await broker.ConfirmAsync(request, cancellationToken);
        }
        finally
        {
            setState(ServerState.Starting);
        }
    }

    public async Task<string?> ChooseAsync(
        IReadOnlyList<SessionSummary> sessions,
        string? currentId,
        bool listWorkspace,
        CancellationToken cancellationToken)
    {
        setState(ServerState.AwaitingOperator);
        try
        {
            return await broker.ChooseAsync(sessions, currentId, listWorkspace, cancellationToken);
        }
        finally
        {
            setState(ServerState.Starting);
        }
    }
}
