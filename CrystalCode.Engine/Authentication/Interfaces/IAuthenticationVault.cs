namespace CrystalCode.Engine.Authentication.Interfaces;

/// <summary>
/// One provider's slot in <c>authentication-credentials.json</c>. The engine
/// treats the data as opaque JSON text owned by the integration.
/// </summary>
public interface IAuthenticationVault
{
    /// <summary>
    /// Reads the saved JSON text, or null when the provider is not signed in.
    /// </summary>
    Task<string?> ReadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Saves JSON text atomically with owner-only access.
    /// </summary>
    Task WriteAsync(string json, CancellationToken cancellationToken);

    /// <summary>
    /// Runs an action while holding the cross-process lock, so one refresh
    /// at a time uses a rotating token. Reads and writes inside the action
    /// do not take the lock again. The lock is not reentrant across calls.
    /// </summary>
    Task<T> RunExclusiveAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken);
}
