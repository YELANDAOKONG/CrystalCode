using Spectre.Console;
using Spectre.Console.Rendering;

using CrystalCode.Display.Paint;
using CrystalCode.Engine.Authentication.Interfaces;

namespace CrystalCode.Terminal;

/// <summary>
/// The sign-in popup of a running session. Steps and the link appear in one
/// card, and the paste field is live from the start, so a browser on another
/// machine needs no extra key. The card closes by itself when the browser
/// answers first. Esc cancels the sign-in, as does Ctrl+C through the session.
/// </summary>
internal sealed class LoginPopup : ILoginInteraction
{
    private readonly SessionRenderer _renderer;
    private readonly string _provider;
    private readonly bool _openBrowser;
    private readonly List<string> _steps = [];
    private (string Label, Uri Link)? _link;
    private string? _problem;

    public LoginPopup(SessionRenderer renderer, string provider, bool openBrowser)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        _renderer = renderer;
        _provider = provider;
        _openBrowser = openBrowser;
    }

    public Task ShowAsync(string text, CancellationToken cancellationToken)
    {
        _steps.Add(text);
        return Task.CompletedTask;
    }

    public Task ShowLinkAsync(string label, Uri link, CancellationToken cancellationToken)
    {
        _link = (label, link);
        return Task.CompletedTask;
    }

    // With the browser off the flow shows the link instead, as it does when opening fails.
    public Task<bool> OpenLinkAsync(Uri link, CancellationToken cancellationToken) =>
        _openBrowser ? BrowserLauncher.OpenAsync(link, cancellationToken) : Task.FromResult(false);

    public async Task<string> ReadAsync(string prompt, CancellationToken cancellationToken)
    {
        _renderer.CloseStream();
        _renderer.PauseComposer();
        try
        {
            while (true)
            {
                var answer = await _renderer.ReadOverlayInputAsync(
                    string.Empty,
                    (text, cursor) => Render(prompt, text, cursor),
                    cancellationToken);
                if (answer is null)
                {
                    throw new OperationCanceledException("Signing in was cancelled.");
                }

                var text = answer.Trim();
                if (IsAddress(text))
                {
                    return text;
                }

                _problem = "That is not an address. Paste the whole address from the browser.";
            }
        }
        finally
        {
            _renderer.ClearOverlay();
            _renderer.ResumeComposer();
        }
    }

    internal static bool IsAddress(string text) =>
        Uri.TryCreate(text, UriKind.Absolute, out var address)
        && (address.Scheme == Uri.UriSchemeHttp || address.Scheme == Uri.UriSchemeHttps);

    private IRenderable Render(string prompt, string text, int cursor)
    {
        var rows = new List<IRenderable>();
        foreach (var step in _steps)
        {
            rows.Add(new Markup($"[{Theme.Chrome}]{MarkupText.Escape(step)}[/]"));
        }

        if (_link is { } shown)
        {
            rows.Add(new Markup($"[{Theme.Muted}]{MarkupText.Escape(shown.Label)}[/]"));
            // Unframed and unwrapped by the panel so the address copies whole.
            rows.Add(new Text(shown.Link.AbsoluteUri) { Overflow = Overflow.Fold });
        }

        rows.Add(new Markup(" "));
        rows.Add(new Markup($"[{Theme.Review} bold]{MarkupText.Escape(prompt)}[/]"));
        if (_problem is not null)
        {
            rows.Add(new Markup($"[{Theme.Fail}]{MarkupText.Escape(_problem)}[/]"));
        }

        rows.Add(new Markup(
            $"[{Theme.User} bold]> [/]{MarkupText.Escape(text[..cursor])}"
            + $"[{Theme.Accent} bold]|[/]{MarkupText.Escape(text[cursor..])}"));
        rows.Add(new Markup($"[{Theme.Muted}]Enter Send  Esc or Ctrl+C Cancel[/]"));
        var panel = new Panel(new Rows(rows))
        {
            Header = new PanelHeader($"Sign in  {MarkupText.Escape(_provider)}"),
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse(Theme.Chrome),
            Padding = new Padding(1, 0, 1, 0),
            Expand = true
        };
        return new Padder(panel, new Padding(2, 0, 0, 0));
    }
}
