using CrystalCode.Engine.Authentication.Interfaces;
using CrystalCode.Plugins.Authentication.Interfaces;

namespace CrystalCode.Engine.Plugins.Disk;

/// <summary>Hands a plugin its provider's slot in the saved logins.</summary>
internal sealed class DiskVault : IPluginVault
{
    private readonly IAuthenticationVault _vault;

    public DiskVault(IAuthenticationVault vault)
    {
        _vault = vault;
    }

    public Task<string?> ReadAsync(CancellationToken cancellationToken) => _vault.ReadAsync(cancellationToken);

    public Task WriteAsync(string json, CancellationToken cancellationToken) => _vault.WriteAsync(json, cancellationToken);

    public Task<T> RunExclusiveAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken) =>
        _vault.RunExclusiveAsync(action, cancellationToken);
}
