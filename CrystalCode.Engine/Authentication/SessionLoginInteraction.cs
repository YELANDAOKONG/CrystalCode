using CrystalCode.Engine.Authentication.Interfaces;
using CrystalCode.Engine.Tools.Questions;
using CrystalCode.Engine.Tools.Questions.Interfaces;

namespace CrystalCode.Engine.Authentication;

/// <summary>
/// The sign-in surface a running session offers. Text goes out as notes, the
/// front end opens links, and pasted text comes back through an operator
/// question. The question appears at once when no browser was opened, and
/// after <see cref="PasteDelay"/> when one was, so a sign-in that finishes in
/// the browser never flashes it.
/// </summary>
internal sealed class SessionLoginInteraction : ILoginInteraction
{
    public static readonly TimeSpan PasteDelay = TimeSpan.FromSeconds(10);

    private readonly Action<string> _note;
    private readonly Func<Uri, CancellationToken, Task<bool>> _open;
    private readonly IUserPrompt _questions;
    private readonly bool _openBrowser;
    private readonly TimeSpan _pasteDelay;
    private bool _browserOpened;

    public SessionLoginInteraction(
        Action<string> note,
        Func<Uri, CancellationToken, Task<bool>> open,
        IUserPrompt questions,
        bool openBrowser,
        TimeSpan? pasteDelay = null)
    {
        _note = note;
        _open = open;
        _questions = questions;
        _openBrowser = openBrowser;
        _pasteDelay = pasteDelay ?? PasteDelay;
    }

    public Task ShowAsync(string text, CancellationToken cancellationToken)
    {
        _note(text);
        return Task.CompletedTask;
    }

    public async Task<string> ReadAsync(string prompt, CancellationToken cancellationToken)
    {
        if (_browserOpened)
        {
            await Task.Delay(_pasteDelay, cancellationToken).ConfigureAwait(false);
        }

        while (true)
        {
            var response = await _questions.AskAsync(
                [new UserQuestion("Sign in", prompt, [], Multiple: false, Custom: true)],
                cancellationToken).ConfigureAwait(false);
            if (response.IsRejected
                || response.IsUnavailable
                || response.Answers.Count == 0
                || response.Answers[0].Count == 0)
            {
                throw new AuthenticationFailedException("Signing in was cancelled.");
            }

            var text = response.Answers[0][0].Trim();
            if (Uri.TryCreate(text, UriKind.Absolute, out var address)
                && (address.Scheme == Uri.UriSchemeHttp || address.Scheme == Uri.UriSchemeHttps))
            {
                return text;
            }

            _note("That is not an address. Paste the whole address from the browser, or press Esc to cancel.");
        }
    }

    public async Task<bool> OpenLinkAsync(Uri link, CancellationToken cancellationToken)
    {
        if (!_openBrowser)
        {
            return false;
        }

        _browserOpened = await _open(link, cancellationToken).ConfigureAwait(false);
        return _browserOpened;
    }
}
