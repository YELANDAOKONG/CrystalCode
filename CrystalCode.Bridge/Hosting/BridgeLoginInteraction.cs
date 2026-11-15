using CrystalCode.Engine.Authentication.Interfaces;

namespace CrystalCode.Bridge.Hosting;

/// <summary>
/// The sign-in surface of <c>crystal bridge</c>. Bridge never opens a browser,
/// because ACP has no way to ask the editor to. With form support the link and
/// steps go in an <c>elicitation/create</c> form whose one field takes the
/// address the browser ended on; the callback finishing first closes the form.
/// Without forms the link is sent as agent text and only the callback on this
/// machine can finish, so the operator cancels with <c>session/cancel</c>.
/// </summary>
internal sealed class BridgeLoginInteraction : ILoginInteraction
{
    private readonly BridgeSession _session;
    private readonly string _provider;
    private readonly List<string> _steps = [];
    private (string Label, Uri Link)? _link;

    public BridgeLoginInteraction(BridgeSession session, string provider)
    {
        _session = session;
        _provider = provider;
    }

    public Task ShowAsync(string text, CancellationToken cancellationToken)
    {
        _steps.Add(text);
        if (!_session.FormsAvailable)
        {
            _session.ShowText(text);
        }

        return Task.CompletedTask;
    }

    public Task ShowLinkAsync(string label, Uri link, CancellationToken cancellationToken)
    {
        _link = (label, link);
        if (!_session.FormsAvailable)
        {
            _session.ShowText(label + "\n" + link.AbsoluteUri);
        }

        return Task.CompletedTask;
    }

    public Task<bool> OpenLinkAsync(Uri link, CancellationToken cancellationToken) =>
        Task.FromResult(false);

    public async Task<string> ReadAsync(string prompt, CancellationToken cancellationToken)
    {
        if (!_session.FormsAvailable)
        {
            // Only the callback on this machine or a cancel ends the wait.
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return string.Empty;
        }

        var address = await _session.AskLoginAsync(_provider, _steps, _link, prompt, cancellationToken)
            .ConfigureAwait(false);
        return address ?? throw new OperationCanceledException("Signing in was cancelled.");
    }
}
