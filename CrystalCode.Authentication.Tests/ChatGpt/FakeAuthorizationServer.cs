using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CrystalCode.Authentication.Tests.ChatGpt;

/// <summary>
/// Stands in for the authorization server: discovery, signing keys, and the
/// token endpoint. Requests are recorded so tests can check what was sent.
/// </summary>
internal sealed class FakeAuthorizationServer : HttpMessageHandler
{
    public const string IssuedClientId = "oaiapp_test";
    public const string Subject = "subject-1";

    private readonly RSA _key = RSA.Create(2048);
    private int _tokenCounter;

    public List<Dictionary<string, string>> TokenRequests { get; } = [];

    /// <summary>
    /// Gets or sets the nonce the next ID token carries.
    /// </summary>
    public string Nonce { get; set; } = string.Empty;

    public string TokenSubject { get; set; } = Subject;

    public string Scope { get; set; } =
        "openid profile email offline_access resource.invoke chatgpt.tokens.use.direct";

    public string? RefreshError { get; set; }

    public int ExpiresIn { get; set; } = 3600;

    public string TokenAudience { get; set; } = IssuedClientId;

    public TimeSpan TokenDelay { get; set; } = TimeSpan.Zero;

    public int RefreshCount => TokenRequests.Count(request => request["grant_type"] == "refresh_token");

    public string AccessTokenFor(int number) => $"access-{number}";

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        if (url == "https://auth.openai.com/.well-known/openid-configuration")
        {
            return Json("""{"jwks_uri":"https://auth.openai.com/.well-known/jwks.json"}""");
        }

        if (url == "https://auth.openai.com/.well-known/jwks.json")
        {
            var parameters = _key.ExportParameters(false);
            return Json(JsonSerializer.Serialize(new
            {
                keys = new[]
                {
                    new
                    {
                        kty = "RSA",
                        kid = "k1",
                        n = Base64Url(parameters.Modulus!),
                        e = Base64Url(parameters.Exponent!)
                    }
                }
            }));
        }

        if (url == "https://auth.openai.com/api/accounts/oauth/token")
        {
            var form = (await request.Content!.ReadAsStringAsync(cancellationToken))
                .Split('&')
                .Select(pair => pair.Split('=', 2))
                .ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair[1].Replace('+', ' ')));
            lock (TokenRequests)
            {
                TokenRequests.Add(form);
            }

            if (TokenDelay > TimeSpan.Zero)
            {
                await Task.Delay(TokenDelay, cancellationToken);
            }

            if (form["grant_type"] == "refresh_token" && RefreshError is not null)
            {
                return Json($$"""{"error":"{{RefreshError}}"}""", System.Net.HttpStatusCode.BadRequest);
            }

            var number = Interlocked.Increment(ref _tokenCounter);
            return Json(JsonSerializer.Serialize(new
            {
                access_token = AccessTokenFor(number),
                refresh_token = $"refresh-{number}",
                id_token = SignIdToken(),
                token_type = "Bearer",
                expires_in = ExpiresIn,
                scope = Scope
            }));
        }

        return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
    }

    public string SignIdToken(
        string? nonce = null,
        string? audience = null,
        string issuer = "https://auth.openai.com",
        long? expires = null,
        RSA? key = null)
    {
        var header = Base64Url(Encoding.UTF8.GetBytes("""{"alg":"RS256","kid":"k1"}"""));
        var payload = Base64Url(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            iss = issuer,
            aud = audience ?? TokenAudience,
            sub = TokenSubject,
            email = "person@example.test",
            nonce = nonce ?? Nonce,
            exp = expires ?? DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
        })));
        var signature = (key ?? _key).SignData(
            Encoding.ASCII.GetBytes(header + "." + payload),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        return $"{header}.{payload}.{Base64Url(signature)}";
    }

    private static HttpResponseMessage Json(
        string body,
        System.Net.HttpStatusCode status = System.Net.HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
