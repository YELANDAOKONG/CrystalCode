using CrystalCode.Engine.Home;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Sessions.Interfaces;

namespace CrystalCode.Engine.Tests.Sessions;

internal sealed class DecliningSessionChooser : ISessionChooser
{
    public Task<string?> ChooseAsync(
        IReadOnlyList<SessionSummary> sessions,
        string? currentId,
        bool listWorkspace,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<string?>(null);
    }
}
