using CrystalCode.Engine.Authentication.Interfaces;

namespace CrystalCode.Engine.Tests.Authentication;

/// <summary>
/// A sign-in surface for session tests. It records what the flow shows and can
/// answer the way a browser, a paste, or an operator pressing Esc would.
/// </summary>
internal sealed class ScriptedLogin : ILoginInteraction
{
    public ScriptedLogin(bool opensBrowser = true)
    {
        OpensBrowser = opensBrowser;
    }

    public bool OpensBrowser { get; }

    public bool OpenBrowserRequested { get; init; } = true;

    public List<string> Shown { get; } = [];

    public int Opened { get; private set; }

    public Task ShowAsync(string text, CancellationToken cancellationToken)
    {
        Shown.Add(text);
        return Task.CompletedTask;
    }

    public Task ShowLinkAsync(string label, Uri link, CancellationToken cancellationToken)
    {
        Shown.Add(label + " " + link);
        return Task.CompletedTask;
    }

    public Task<string> ReadAsync(string prompt, CancellationToken cancellationToken) =>
        Task.FromResult(string.Empty);

    public Task<bool> OpenLinkAsync(Uri link, CancellationToken cancellationToken)
    {
        Opened++;
        return Task.FromResult(OpenBrowserRequested && OpensBrowser);
    }
}
