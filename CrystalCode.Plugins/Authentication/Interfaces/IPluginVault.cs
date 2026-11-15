namespace CrystalCode.Plugins.Authentication.Interfaces;

/// <summary>
/// One provider's slot in the host's saved logins. The host treats the data
/// as opaque JSON text owned by the integration and writes it atomically with
/// owner-only access. Never log what it holds.
/// </summary>
public interface IPluginVault
{
    /// <summary>Reads the saved JSON text, or null when the provider is not signed in.</summary>
    Task<string?> ReadAsync(CancellationToken cancellationToken);

    /// <summary>Saves JSON text.</summary>
    Task WriteAsync(string json, CancellationToken cancellationToken);

    /// <summary>
    /// Runs an action while holding the cross-process lock, so one renewal at
    /// a time uses a rotating token. Reads and writes inside the action do not
    /// take the lock again, and the lock is not reentrant across calls.
    /// </summary>
    Task<T> RunExclusiveAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken);
}
