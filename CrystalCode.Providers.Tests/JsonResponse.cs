using System.Net;
using System.Text;

namespace CrystalCode.Providers.Tests;

internal static class JsonResponse
{
    public static HttpResponseMessage Create(
        string json,
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    public static HttpResponseMessage CreateStream(string events)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(events);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(events, Encoding.UTF8, "text/event-stream")
        };
    }
}
