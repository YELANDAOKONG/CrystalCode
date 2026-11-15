using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CrystalCode.Authentication.ChatGpt;

/// <summary>
/// A one-shot HTTP listener on the loopback address that waits for the
/// browser to come back to the callback path.
/// </summary>
internal sealed class LoopbackCallback : IDisposable
{
    private const string Page =
        "<!doctype html><title>Crystal Code</title><p>Sign-in is complete. You can close this tab.</p>";

    private readonly HttpListener _listener;
    private readonly TaskCompletionSource<Uri> _result =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private LoopbackCallback(HttpListener listener, int port)
    {
        _listener = listener;
        RedirectUri = new Uri($"http://127.0.0.1:{port}{ChatGptEndpoints.CallbackPath}");
    }

    /// <summary>
    /// Gets the exact callback address to send in both OAuth requests.
    /// </summary>
    public Uri RedirectUri { get; }

    public static LoopbackCallback Start(int preferredPort)
    {
        foreach (var port in new[] { preferredPort, FreePort() })
        {
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            try
            {
                listener.Start();
            }
            catch (HttpListenerException)
            {
                listener.Close();
                continue;
            }

            var callback = new LoopbackCallback(listener, port);
            _ = callback.ServeAsync();
            return callback;
        }

        throw new InvalidOperationException("No loopback port was available for the sign-in callback.");
    }

    /// <summary>
    /// Waits for the browser to arrive. The result is the full request address.
    /// </summary>
    public async Task<Uri> WaitAsync(CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(
            static state => ((TaskCompletionSource<Uri>)state!).TrySetCanceled(),
            _result);
        return await _result.Task.ConfigureAwait(false);
    }

    public void Dispose() => _listener.Close();

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try
        {
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
        }
    }

    private async Task ServeAsync()
    {
        try
        {
            while (_listener.IsListening && !_result.Task.IsCompleted)
            {
                var context = await _listener.GetContextAsync().ConfigureAwait(false);
                var matched = context.Request.Url is { } url
                    && string.Equals(url.AbsolutePath, ChatGptEndpoints.CallbackPath, StringComparison.Ordinal);
                var body = Encoding.UTF8.GetBytes(matched ? Page : string.Empty);
                context.Response.StatusCode = matched ? 200 : 404;
                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.OutputStream.WriteAsync(body).ConfigureAwait(false);
                context.Response.Close();
                if (matched)
                {
                    _result.TrySetResult(context.Request.Url!);
                }
            }
        }
        catch (Exception exception) when (
            exception is HttpListenerException or ObjectDisposedException or InvalidOperationException)
        {
            // The listener was closed after the attempt ended.
        }
    }
}
