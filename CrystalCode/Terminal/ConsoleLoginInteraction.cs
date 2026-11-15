using Spectre.Console;

using CrystalCode.Display.Paint;
using CrystalCode.Engine.Authentication.Interfaces;

namespace CrystalCode.Terminal;

/// <summary>
/// The sign-in surface of <c>crystal login</c>. Formatted output colors the
/// lines and keeps the link on a line of its own so it copies whole; plain
/// output writes bare lines. Links open in the default browser unless the
/// operator turned that off, and the operator may paste into a line.
/// </summary>
public sealed class ConsoleLoginInteraction : ILoginInteraction
{
    private readonly TextWriter _output;
    private readonly TextReader _input;
    private readonly bool _openBrowser;
    private readonly bool _plain;
    private readonly IAnsiConsole? _console;

    public ConsoleLoginInteraction(
        TextWriter output,
        TextReader input,
        bool openBrowser = true,
        bool plain = true)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(input);
        _output = output;
        _input = input;
        _openBrowser = openBrowser;
        _plain = plain;
        if (!plain)
        {
            _console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(output) });
        }
    }

    public async Task ShowAsync(string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_console is null)
        {
            await _output.WriteLineAsync(text.AsMemory(), cancellationToken);
            return;
        }

        _console.MarkupLine($"[{Theme.Chrome}]{MarkupText.Escape(text)}[/]");
    }

    public async Task ShowLinkAsync(string label, Uri link, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_console is null)
        {
            await _output.WriteLineAsync(label.AsMemory(), cancellationToken);
            await _output.WriteLineAsync(link.AbsoluteUri.AsMemory(), cancellationToken);
            return;
        }

        _console.MarkupLine($"[{Theme.Muted}]{MarkupText.Escape(label)}[/]");
        // Written outside Spectre so it is never wrapped or restyled and copies whole.
        await _output.WriteLineAsync(link.AbsoluteUri.AsMemory(), cancellationToken);
    }

    public async Task<string> ReadAsync(string prompt, CancellationToken cancellationToken)
    {
        if (_console is null)
        {
            await _output.WriteLineAsync(prompt.AsMemory(), cancellationToken);
        }
        else
        {
            _console.MarkupLine($"[{Theme.Review} bold]{MarkupText.Escape(prompt)}[/]");
        }

        var line = await _input.ReadLineAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(line))
        {
            return line;
        }

        // No terminal input (closed or redirected): only the browser can finish the sign-in.
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return string.Empty;
    }

    public Task<bool> OpenLinkAsync(Uri link, CancellationToken cancellationToken) =>
        _openBrowser ? BrowserLauncher.OpenAsync(link, cancellationToken) : Task.FromResult(false);
}
