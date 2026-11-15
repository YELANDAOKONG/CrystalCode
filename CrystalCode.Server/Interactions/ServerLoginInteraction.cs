using System.Text.Json;

using CrystalCode.Engine.Authentication.Interfaces;

namespace CrystalCode.Server.Interactions;

/// <summary>
/// The sign-in surface of <c>crystal server</c>. The server never opens a
/// browser, because it may run on another machine than the operator: the
/// flow shows the link, and the client opens it. The callback finishes the
/// sign-in by itself when the browser can reach this machine; otherwise the
/// client answers the <c>login</c> interaction with the address the browser
/// ended on, or with <c>cancelled</c>.
/// </summary>
internal sealed class ServerLoginInteraction : ILoginInteraction
{
    private readonly InteractionBroker _broker;
    private readonly string _provider;
    private readonly List<string> _steps = [];
    private (string Label, Uri Link)? _link;

    public ServerLoginInteraction(InteractionBroker broker, string provider)
    {
        _broker = broker;
        _provider = provider;
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

    // The client opens the link on the operator's machine, so the server never does.
    public Task<bool> OpenLinkAsync(Uri link, CancellationToken cancellationToken) =>
        Task.FromResult(false);

    public async Task<string> ReadAsync(string prompt, CancellationToken cancellationToken)
    {
        var address = await _broker.AskLoginAsync(_provider, _steps, _link, prompt, cancellationToken);
        return address ?? throw new OperationCanceledException("Signing in was cancelled.");
    }

    internal static (bool Valid, string? Address, string Error) ParseAnswer(JsonElement answer)
    {
        if (answer.ValueKind != JsonValueKind.Object)
        {
            return (false, null, "The answer must be an object.");
        }

        if (answer.TryGetProperty("cancelled", out var cancelled) && cancelled.ValueKind == JsonValueKind.True)
        {
            return (true, null, string.Empty);
        }

        var text = answer.TryGetProperty("address", out var address) && address.ValueKind == JsonValueKind.String
            ? address.GetString()?.Trim()
            : null;
        return text is not null
            && Uri.TryCreate(text, UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
                ? (true, text, string.Empty)
                : (false, null, "address must be the http or https address the browser ended on, or pass cancelled: true.");
    }
}
