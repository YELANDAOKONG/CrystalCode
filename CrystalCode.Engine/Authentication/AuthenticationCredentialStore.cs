using System.Text.Json;

using CrystalCode.Engine.Authentication.Interfaces;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Home;

namespace CrystalCode.Engine.Authentication;

/// <summary>
/// Reads and writes <c>authentication-credentials.json</c>: one entry per
/// provider name holding the integration id and opaque JSON data. The file is
/// written atomically with owner-only access. A lock file beside it
/// serializes writers across processes.
/// </summary>
public sealed class AuthenticationCredentialStore
{
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LockRetryDelay = TimeSpan.FromMilliseconds(50);
    private static readonly AsyncLocal<bool> LockHeld = new();

    private readonly CrystalHome _home;

    public AuthenticationCredentialStore(CrystalHome home)
    {
        ArgumentNullException.ThrowIfNull(home);
        _home = home;
    }

    /// <summary>
    /// Gets whether the provider has a saved login for its current integration.
    /// </summary>
    public bool IsSignedIn(ProviderDefinition provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (provider.Integration is null)
        {
            return false;
        }

        return ReadDocument().TryGetValue(provider.Name.Value, out var entry)
            && entry.Data is not null
            && string.Equals(entry.Integration, provider.Integration, StringComparison.Ordinal);
    }

    public IAuthenticationVault Open(ProviderDefinition provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (provider.Integration is null)
        {
            throw new ArgumentException("The provider has no integration.", nameof(provider));
        }

        return new AuthenticationVault(this, provider.Name, provider.Integration);
    }

    /// <summary>
    /// Removes a provider's saved login. Returns false when there was none.
    /// </summary>
    public async Task<bool> RemoveAsync(
        ProviderName provider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        using var held = await AcquireAsync(cancellationToken).ConfigureAwait(false);
        var document = ReadDocument();
        if (!document.Remove(provider.Value))
        {
            return false;
        }

        WriteDocument(document);
        return true;
    }

    internal string? ReadData(ProviderName provider)
    {
        return ReadDocument().TryGetValue(provider.Value, out var entry)
            ? entry.Data?.GetRawText()
            : null;
    }

    internal async Task WriteDataAsync(
        ProviderName provider,
        string integration,
        string json,
        CancellationToken cancellationToken)
    {
        using var data = JsonDocument.Parse(json);
        using var held = await AcquireAsync(cancellationToken).ConfigureAwait(false);
        var document = ReadDocument();
        document[provider.Value] = new AuthenticationEntryDocument
        {
            Integration = integration,
            Data = data.RootElement.Clone()
        };
        WriteDocument(document);
    }

    internal async Task<T> RunExclusiveAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        if (LockHeld.Value)
        {
            throw new InvalidOperationException("The credential lock is already held.");
        }

        using var held = await AcquireAsync(cancellationToken).ConfigureAwait(false);
        // Set here, not in AcquireAsync: a value set inside an awaited async
        // method does not flow back to its caller.
        LockHeld.Value = true;
        try
        {
            return await action(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            LockHeld.Value = false;
        }
    }

    private async Task<LockHandle> AcquireAsync(CancellationToken cancellationToken)
    {
        // An action that holds the lock may read and write without waiting on itself.
        if (LockHeld.Value)
        {
            return LockHandle.None;
        }

        _home.EnsureCreated();
        var options = new FileStreamOptions
        {
            Mode = FileMode.OpenOrCreate,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
            BufferSize = 1
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        var started = DateTime.UtcNow;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var stream = new FileStream(_home.AuthenticationCredentialsLockPath, options);
                return new LockHandle(stream);
            }
            catch (IOException) when (DateTime.UtcNow - started < LockTimeout)
            {
                await Task.Delay(LockRetryDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException exception)
            {
                throw new AuthenticationFailedException(
                    "Timed out waiting for the saved login file lock.",
                    exception);
            }
        }
    }

    private Dictionary<string, AuthenticationEntryDocument> ReadDocument()
    {
        var path = _home.AuthenticationCredentialsPath;
        if (!File.Exists(path))
        {
            return new Dictionary<string, AuthenticationEntryDocument>(StringComparer.Ordinal);
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, AuthenticationEntryDocument>>(
                    File.ReadAllText(path),
                    HomeJson.Options)
                ?? new Dictionary<string, AuthenticationEntryDocument>(StringComparer.Ordinal);
        }
        catch (JsonException exception)
        {
            // The message carries no file contents.
            throw new InvalidOperationException(
                $"{path} is not valid JSON. Fix or delete it, then sign in again.",
                exception);
        }
    }

    private void WriteDocument(Dictionary<string, AuthenticationEntryDocument> document)
    {
        _home.EnsureCreated();
        AtomicFile.WriteText(
            _home.AuthenticationCredentialsPath,
            JsonSerializer.Serialize(document, HomeJson.Options),
            ownerOnly: true,
            followLink: true);
    }

    private sealed class LockHandle : IDisposable
    {
        private readonly FileStream? _stream;

        public LockHandle(FileStream? stream)
        {
            _stream = stream;
        }

        public static LockHandle None { get; } = new(null);

        public void Dispose()
        {
            if (_stream is null)
            {
                return;
            }

            _stream.Dispose();
        }
    }
}
