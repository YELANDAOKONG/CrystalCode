using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

using Crystal.Chat;
using CrystalCode.Providers.Compatible;

namespace CrystalCode.Providers.Protocol;

internal sealed class ProtocolChatClient : IStreamingChatClient, IDisposable
{
    private readonly IProtocolCodec _codec;
    private readonly ProtocolOptions _options;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public ProtocolChatClient(
        IProtocolCodec codec,
        ProtocolOptions options,
        HttpClient? httpClient)
    {
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(options);
        _codec = codec;
        _options = options;
        if (httpClient is null)
        {
            _httpClient = new HttpClient { Timeout = options.RequestTimeout };
            _ownsHttpClient = true;
        }
        else
        {
            _httpClient = httpClient;
        }
    }

    public async Task<ChatResponse> CompleteAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = _codec.WriteRequest(_options, request, stream: false);
        using var response = await SendAsync(body, stream: false, cancellationToken)
            .ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            using var document = JsonDocument.Parse(payload);
            return _codec.ReadResponse(document.RootElement);
        }
        catch (JsonException exception)
        {
            throw _codec.CreateException(
                $"{_options.VendorName} response was not valid JSON.",
                innerException: exception);
        }
    }

    public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = _codec.WriteRequest(_options, request, stream: true);
        using var response = await SendAsync(body, stream: true, cancellationToken)
            .ConfigureAwait(false);
        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var reader = new StreamReader(content, Encoding.UTF8);
        var parser = _codec.CreateStreamParser();
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var data = line[5..].TrimStart();
            if (data.Length == 0 || data == "[DONE]")
            {
                continue;
            }

            IReadOnlyList<ChatStreamEvent> events;
            try
            {
                using var document = JsonDocument.Parse(data);
                events = parser.Parse(document.RootElement);
            }
            catch (JsonException exception)
            {
                throw _codec.CreateException(
                    $"{_options.VendorName} stream contained invalid JSON.",
                    innerException: exception);
            }

            foreach (var streamEvent in events)
            {
                yield return streamEvent;
            }
        }

        if (!parser.IsComplete)
        {
            throw _codec.CreateException(
                $"{_options.VendorName} stream ended before the candidate completed.");
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        byte[] body,
        bool stream,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(_options.BaseUri, _codec.Path))
        {
            Content = new ByteArrayContent(body)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
        {
            CharSet = "utf-8"
        };
        request.Headers.UserAgent.ParseAdd(CompatibleWire.UserAgent);
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue(stream ? "text/event-stream" : "application/json"));
        _codec.AddHeaders(request, _options.ApiKey);

        HttpResponseMessage? response = null;
        try
        {
            response = await _httpClient.SendAsync(
                request,
                stream ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead,
                cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            var bodyText = await response.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
            var (code, providerMessage) = CompatibleWire.ReadError(bodyText);
            var status = (int)response.StatusCode;
            var message = code is null
                ? $"{_options.VendorName} request failed (HTTP {status})."
                : $"{_options.VendorName} request failed (HTTP {status}, code: {code}).";
            if (!string.IsNullOrWhiteSpace(providerMessage))
            {
                message += $" {providerMessage}";
            }

            throw _codec.CreateException(
                message,
                status,
                errorCode: code,
                retryAfter: CompatibleRetryAfter.Read(response.Headers));
        }
        catch (OperationCanceledException)
        {
            response?.Dispose();
            throw;
        }
        catch (ChatProviderException)
        {
            response?.Dispose();
            throw;
        }
        catch (Exception exception) when (exception is not ChatProviderException)
        {
            response?.Dispose();
            throw _codec.CreateException(
                $"{_options.VendorName} request failed.",
                innerException: exception);
        }
    }
}
