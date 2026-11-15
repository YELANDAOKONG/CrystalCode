using CrystalCode.Engine.Authentication.Interfaces;
using CrystalCode.Plugins.Authentication.Interfaces;

namespace CrystalCode.Engine.Plugins.Disk;

/// <summary>Hands a plugin the front end's sign-in surface.</summary>
internal sealed class DiskLogin : IPluginLogin
{
    private readonly ILoginInteraction _interaction;

    public DiskLogin(ILoginInteraction interaction)
    {
        _interaction = interaction;
    }

    public Task ShowAsync(string text, CancellationToken cancellationToken) =>
        _interaction.ShowAsync(text, cancellationToken);

    public Task ShowLinkAsync(string label, Uri link, CancellationToken cancellationToken) =>
        _interaction.ShowLinkAsync(label, link, cancellationToken);

    public Task<string> ReadAsync(string prompt, CancellationToken cancellationToken) =>
        _interaction.ReadAsync(prompt, cancellationToken);

    public Task<bool> OpenLinkAsync(Uri link, CancellationToken cancellationToken) =>
        _interaction.OpenLinkAsync(link, cancellationToken);
}
