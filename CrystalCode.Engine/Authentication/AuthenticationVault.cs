using CrystalCode.Engine.Authentication.Interfaces;
using CrystalCode.Engine.Configuration;

namespace CrystalCode.Engine.Authentication;

internal sealed class AuthenticationVault : IAuthenticationVault
{
    private readonly AuthenticationCredentialStore _store;
    private readonly ProviderName _provider;
    private readonly string _integration;

    public AuthenticationVault(
        AuthenticationCredentialStore store,
        ProviderName provider,
        string integration)
    {
        _store = store;
        _provider = provider;
        _integration = integration;
    }

    public Task<string?> ReadAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_store.ReadData(_provider));

    public Task WriteAsync(string json, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return _store.WriteDataAsync(_provider, _integration, json, cancellationToken);
    }

    public Task<T> RunExclusiveAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        return _store.RunExclusiveAsync(action, cancellationToken);
    }
}
