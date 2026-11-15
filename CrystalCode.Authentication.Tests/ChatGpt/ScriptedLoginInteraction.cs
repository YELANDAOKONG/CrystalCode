using System.Web;

using CrystalCode.Engine.Authentication.Interfaces;

namespace CrystalCode.Authentication.Tests.ChatGpt;

/// <summary>
/// An operator and a browser in one: it records what the flow shows and, when
/// asked to open the link, plays the browser by calling the callback address.
/// </summary>
internal sealed class ScriptedLoginInteraction : ILoginInteraction
{
    private readonly FakeAuthorizationServer _server;
    private readonly TaskCompletionSource<string> _paste = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ScriptedLoginInteraction(FakeAuthorizationServer server)
    {
        _server = server;
    }

    public List<string> Shown { get; } = [];

    public Uri? OpenedLink { get; private set; }

    /// <summary>
    /// Gets or sets whether opening the link reaches a browser on this machine.
    /// </summary>
    public bool BrowserWorks { get; set; } = true;

    public string ClientIdInCallback { get; set; } = FakeAuthorizationServer.IssuedClientId;

    public string? ErrorInCallback { get; set; }

    public bool CorruptState { get; set; }

    public bool PasteWhenAsked { get; set; }

    public Task ShowAsync(string text, CancellationToken cancellationToken)
    {
        Shown.Add(text);
        return Task.CompletedTask;
    }

    public async Task<string> ReadAsync(string prompt, CancellationToken cancellationToken)
    {
        Shown.Add(prompt);
        using var registration = cancellationToken.Register(() => _paste.TrySetCanceled());
        if (PasteWhenAsked)
        {
            _paste.TrySetResult(CallbackAddress());
        }

        return await _paste.Task;
    }

    public async Task<bool> OpenLinkAsync(Uri link, CancellationToken cancellationToken)
    {
        OpenedLink = link;
        _server.Nonce = Query("nonce");
        if (!BrowserWorks)
        {
            return false;
        }

        using var http = new HttpClient();
        using var response = await http.GetAsync(CallbackAddress(), cancellationToken);
        return true;
    }

    public string Query(string name) => HttpUtility.ParseQueryString(OpenedLink!.Query)[name]!;

    public string CallbackAddress()
    {
        var state = CorruptState ? "wrong-state" : Query("state");
        var query = ErrorInCallback is null
            ? $"code=the-code&state={Uri.EscapeDataString(state)}&client_id={ClientIdInCallback}"
            : $"error={ErrorInCallback}&state={Uri.EscapeDataString(state)}";
        return $"{Query("redirect_uri")}?{query}";
    }
}
