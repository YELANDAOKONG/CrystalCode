namespace CrystalCode.Providers.Tests;

internal sealed class RecordingHandler : HttpMessageHandler
{
    private readonly HttpResponseMessage _response;

    public RecordingHandler(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        _response = response;
    }

    public HttpRequestMessage? Request { get; private set; }

    public string? Body { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Request = request;
        if (request.Content is not null)
        {
            Body = await request.Content
                .ReadAsStringAsync(cancellationToken);
        }

        return _response;
    }
}
