using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

using Crystal.Chat;

namespace CrystalCode.Providers.Compatible;

internal sealed class CompatibleChatClient : IStreamingChatClient, IDisposable
{
    private readonly CompatibleProfile _profile;
    private readonly CompatibleOptions _options;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public CompatibleChatClient(
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
            _httpClient = new HttpClient
            {
                Timeout = options.RequestTimeout
            };
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

        var body = CompatibleChatRequestWriter.Write(_profile, _options, request, stream: false);
        using var response = await SendAsync(body, stream: false, cancellationToken)
            .ConfigureAwait(false);
        var payload = await response.Content
            .ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            using var document = JsonDocument.Parse(payload);
            return CompatibleChatResponseReader.Read(_profile, document.RootElement);
        }
        catch (JsonException exception)
        {
            throw _profile.Faults.Create(
                $"{_profile.VendorName} chat response was not valid JSON.",
                innerException: exception);
        }
    }

    public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var body = CompatibleChatRequestWriter.Write(_profile, _options, request, stream: true);
        using var response = await SendAsync(body, stream: true, cancellationToken)
            .ConfigureAwait(false);
        await using var content = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var reader = new StreamReader(content, Encoding.UTF8);
        var parser = new CompatibleChatStreamParser(_profile);

        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            if (line.Length == 0 || line[0] == ':')
            {
                continue;
            }

            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var data = line[5..].TrimStart();
            if (data == "[DONE]")
            {
                break;
            }

            IReadOnlyList<ChatStreamEvent> events;
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
                yield return streamEvent;
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
        var uri = new Uri(_options.BaseUri, _profile.ChatCompletionsPath);
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new ByteArrayContent(body)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
        {
            CharSet = "utf-8"
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        // Product tokens cannot contain a space, so the typed User-Agent parser rejects this value.
        request.Headers.TryAddWithoutValidation("User-Agent", CompatibleWire.UserAgent);
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue(stream ? "text/event-stream" : "application/json"));
        if (!string.IsNullOrWhiteSpace(_options.Organization))
        {
            request.Headers.TryAddWithoutValidation("OpenAI-Organization", _options.Organization);
        }

        if (!string.IsNullOrWhiteSpace(_options.Project))
        {
            request.Headers.TryAddWithoutValidation("OpenAI-Project", _options.Project);
        }

        var completionOption = stream
            ? HttpCompletionOption.ResponseHeadersRead
            : HttpCompletionOption.ResponseContentRead;

        HttpResponseMessage? response = null;
        try
        {
            response = await _httpClient
                .SendAsync(request, completionOption, cancellationToken)
                .ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            var errorBody = await response.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
            throw _profile.Faults.FromResponse(response.StatusCode, errorBody);
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
