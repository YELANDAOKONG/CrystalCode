using CrystalCode.Engine.Authentication.Interfaces;

namespace CrystalCode.Terminal;

/// <summary>
/// The sign-in surface of <c>crystal login</c>: plain lines on the console,
/// the default browser for links, and a line the operator may paste into.
/// </summary>
public sealed class ConsoleLoginInteraction : ILoginInteraction
{
    private readonly TextWriter _output;
    private readonly TextReader _input;

    public ConsoleLoginInteraction(TextWriter output, TextReader input)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(input);
        _output = output;
        _input = input;
    }

    public async Task ShowAsync(string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _output.WriteLineAsync(text.AsMemory(), cancellationToken);
    }

    public async Task<string> ReadAsync(string prompt, CancellationToken cancellationToken)
    {
        await _output.WriteLineAsync(prompt.AsMemory(), cancellationToken);
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
        BrowserLauncher.OpenAsync(link, cancellationToken);
}
