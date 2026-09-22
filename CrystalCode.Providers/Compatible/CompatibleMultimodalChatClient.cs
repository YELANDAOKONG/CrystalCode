using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

using Crystal;
using Crystal.Media;
using Crystal.Multimodal;
using Crystal.Multimodal.Chat;

namespace CrystalCode.Providers.Compatible;

internal sealed class CompatibleMultimodalChatClient
    : IStreamingMultimodalChatClient, IDisposable
{
    private readonly CompatibleProfile _profile;
    private readonly CompatibleOptions _options;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public CompatibleMultimodalChatClient(
        CompatibleProfile profile,
        CompatibleOptions options,
        HttpClient? httpClient)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(options);
        _profile = profile;
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

    public MultimodalChatCapabilities Capabilities { get; } = new(
        [
            new MultimodalContentCapability(ContentModality.Text),
            new MultimodalContentCapability(
                ContentModality.Image,
                [MediaSourceKind.Inline, MediaSourceKind.Uri])
        ],
        [new MultimodalContentCapability(ContentModality.Text)],
        supportsTools: true,
        supportsReasoningOptions: true);

    public async Task<MultimodalChatResponse> CompleteAsync(
        MultimodalChatRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = CompatibleMultimodalRequestWriter.Write(
            _profile,
            _options,
            request,
            stream: false);
        using var response = await SendAsync(body, stream: false, cancellationToken)
            .ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            using var document = JsonDocument.Parse(payload);
            return CompatibleMultimodalOutput.Convert(
                CompatibleChatResponseReader.Read(_profile, document.RootElement));
        }
        catch (JsonException exception)
        {
            throw _profile.Faults.Create(
                $"{_profile.VendorName} chat response was not valid JSON.",
                innerException: exception);
        }
    }

    public async IAsyncEnumerable<MultimodalChatStreamEvent> StreamAsync(
        MultimodalChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = CompatibleMultimodalRequestWriter.Write(
            _profile,
            _options,
            request,
            stream: true);
        using var response = await SendAsync(body, stream: true, cancellationToken)
            .ConfigureAwait(false);
        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var reader = new StreamReader(content, Encoding.UTF8);
        var parser = new CompatibleChatStreamParser(_profile);
        var output = new CompatibleMultimodalOutput();
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (line.Length == 0 || line[0] == ':'
                || !line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var data = line[5..].TrimStart();
            if (data == "[DONE]")
            {
                break;
            }

            IReadOnlyList<Crystal.Chat.ChatStreamEvent> events;
            try
            {
                using var document = JsonDocument.Parse(data);
                events = parser.Parse(document.RootElement);
            }
            catch (JsonException exception)
            {
                throw _profile.Faults.Create(
                    $"{_profile.VendorName} chat stream contained a chunk that was not valid JSON.",
                    innerException: exception);
            }

            foreach (var streamEvent in events)
            {
                foreach (var converted in output.Convert(streamEvent))
                {
                    yield return converted;
                }
            }
        }

        if (!parser.IsComplete)
        {
            throw _profile.Faults.Create(
                $"{_profile.VendorName} chat stream ended before every candidate completed.");
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
            new Uri(_options.BaseUri, _profile.ChatCompletionsPath))
        {
            Content = new ByteArrayContent(body)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
        {
            CharSet = "utf-8"
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            _options.ApiKey);
        request.Headers.UserAgent.ParseAdd(CompatibleWire.UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(
            stream ? "text/event-stream" : "application/json"));

        HttpResponseMessage? response = null;
        try
        {
            response = await _httpClient.SendAsync(
                request,
                stream
                    ? HttpCompletionOption.ResponseHeadersRead
                    : HttpCompletionOption.ResponseContentRead,
                cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
            throw _profile.Faults.FromResponse(
                response.StatusCode,
                errorBody,
                response.Headers);
        }
        catch (OperationCanceledException)
        {
            response?.Dispose();
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            response?.Dispose();
            if (_profile.Faults.ExceptionType.IsInstanceOfType(exception))
            {
                throw;
            }

            throw _profile.Faults.Create(
                $"{_profile.VendorName} chat request failed.",
                innerException: exception);
        }
    }
}
