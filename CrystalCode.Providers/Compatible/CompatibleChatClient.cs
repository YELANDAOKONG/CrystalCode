using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

using Crystal.Chat;

using CrystalCode.Providers.Protocol;

namespace CrystalCode.Providers.Compatible;

internal sealed class CompatibleChatClient : IStreamingChatClient, IDisposable
{
    private readonly CompatibleProfile _profile;
    private readonly CompatibleOptions _options;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly IReadOnlyDictionary<string, string> _httpHeaders;

    public CompatibleChatClient(
        CompatibleProfile profile,
        CompatibleOptions options,
        HttpClient? httpClient,
        IReadOnlyDictionary<string, string>? httpHeaders = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(options);

        _profile = profile;
        _options = options;
        _httpHeaders = RequestHeaders.Validate(httpHeaders, profile.ReservedHeaders);
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
        using var tracking = new ProtocolRequestTracking(_options.RequestContext, CompatibleWire.ReadRequestUsage);

        var body = CompatibleChatRequestWriter.Write(_profile, _options, request, stream: false);
        using var response = await SendAsync(tracking, body, stream: false, cancellationToken)
            .ConfigureAwait(false);
        var payload = await response.Content
            .ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            using var document = JsonDocument.Parse(payload);
            tracking.Observe(document.RootElement);
            if (CompatibleWire.TryReadError(document.RootElement, out var code, out var providerMessage))
            {
                throw _profile.Faults.FromResponseError(
                    _profile.VendorName,
                    code,
                    providerMessage);
            }

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
        using var tracking = new ProtocolRequestTracking(_options.RequestContext, CompatibleWire.ReadRequestUsage);

        var body = CompatibleChatRequestWriter.Write(_profile, _options, request, stream: true);
        using var response = await SendAsync(tracking, body, stream: true, cancellationToken)
            .ConfigureAwait(false);
        await using var content = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var reader = new StreamReader(content, Encoding.UTF8);
        var parser = new CompatibleChatStreamParser(_profile);

        await foreach (var data in CompatibleSse.ReadDataAsync(
            reader,
            _profile.Faults,
            _profile.VendorName,
            tracking.Observe,
            cancellationToken))
        {
            foreach (var streamEvent in parser.Parse(data, tracking.Observe))
            {
                yield return streamEvent;
            }
        }

        if (!parser.IsComplete)
        {
            throw _profile.Faults.Create(
                $"{_profile.VendorName} chat stream ended before every candidate completed.");
        }

        if (parser.TakeUsage() is { } usage)
        {
            yield return new ChatUsageReceived(usage);
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
        ProtocolRequestTracking tracking,
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
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }
        request.Headers.UserAgent.ParseAdd(CompatibleWire.UserAgent);
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue(stream ? "text/event-stream" : "application/json"));
        if (!string.IsNullOrWhiteSpace(_options.Organization))
        {
            request.Headers.TryAddWithoutValidation(CompatibleWire.OrganizationHeader, _options.Organization);
        }

        if (!string.IsNullOrWhiteSpace(_options.Project))
        {
            request.Headers.TryAddWithoutValidation(CompatibleWire.ProjectHeader, _options.Project);
        }

        var completionOption = stream
            ? HttpCompletionOption.ResponseHeadersRead
            : HttpCompletionOption.ResponseContentRead;

        RequestHeaders.Apply(request, _httpHeaders);

        cancellationToken.ThrowIfCancellationRequested();
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
            throw _profile.Faults.FromResponse(
                response.StatusCode,
                errorBody,
                response.Headers,
                tracking.Observe);
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
                TransportFailure.Describe(
                    $"{_profile.VendorName} chat request failed.",
                    exception),
                innerException: exception);
        }
        finally
        {
            tracking.RecordAttempts(request);
        }
    }
}
