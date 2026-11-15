using CrystalCode.Engine.Authentication.Interfaces;

namespace CrystalCode.Engine.Authentication;

/// <summary>
/// The sign-in surface a running session offers: notes for text and a
/// front-end callback to open a link. It never asks for pasted text, because
/// a session already has a browser on the same machine; a remote machine
/// signs in with <c>crystal login</c> instead.
/// </summary>
internal sealed class SessionLoginInteraction : ILoginInteraction
{
    private readonly Action<string> _note;
    private readonly Func<Uri, CancellationToken, Task<bool>> _open;

    public SessionLoginInteraction(
        Action<string> note,
        Func<Uri, CancellationToken, Task<bool>> open)
    {
        _note = note;
        _open = open;
    }

    public Task ShowAsync(string text, CancellationToken cancellationToken)
    {
        _note(text);
        return Task.CompletedTask;
    }

    public async Task<string> ReadAsync(string prompt, CancellationToken cancellationToken)
    {
        // Waits for the browser; only cancellation ends this.
        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        return string.Empty;
    }

    public Task<bool> OpenLinkAsync(Uri link, CancellationToken cancellationToken) =>
        _open(link, cancellationToken);
}
