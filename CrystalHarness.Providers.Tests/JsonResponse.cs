using System.Net;
using System.Text;

namespace CrystalHarness.Providers.Tests;

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
}
