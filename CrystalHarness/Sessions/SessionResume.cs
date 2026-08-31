using CrystalHarness.Home;

namespace CrystalHarness.Sessions;

/// <summary>
/// Loads a saved session for <c>/resume</c> and <c>--resume</c>.
/// </summary>
public static class SessionResume
{
    public static bool TryLoad(
        SessionStore store,
        string workspaceRoot,
        string? id,
        out SessionDocument document,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        document = null!;
        error = null!;

        if (string.IsNullOrWhiteSpace(id))
        {
            if (!store.TryLoadLatest(workspaceRoot, out document))
            {
                error = "no session for this workspace";
                return false;
            }
        }
        else if (!store.TryLoad(id, out document))
        {
            error = "session not found  " + id.Trim();
            return false;
        }

        if (TranscriptCodec.Read(document.Items).Count == 0)
        {
            error = "session is empty";
            document = null!;
            return false;
        }

        return true;
    }
}
