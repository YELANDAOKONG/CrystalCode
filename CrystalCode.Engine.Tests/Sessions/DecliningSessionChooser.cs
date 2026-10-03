using CrystalCode.Engine.Home;
using CrystalCode.Engine.Sessions;

namespace CrystalCode.Engine.Tests.Sessions;

internal sealed class DecliningSessionChooser : ISessionChooser
{
    public Task<string?> ChooseAsync(
        IReadOnlyList<SessionSummary> sessions,
        string? currentId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<string?>(null);
    }
}
