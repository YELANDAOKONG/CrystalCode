using System.Security.Cryptography;
using System.Text;

namespace CrystalCode.Authentication.ChatGpt;

/// <summary>
/// One PKCE verifier and its S256 challenge, plus the other random values
/// of a single authorization attempt.
/// </summary>
internal sealed record Pkce(string Verifier, string Challenge, string State, string Nonce)
{
    public static Pkce Create()
    {
        var verifier = RandomText(32);
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return new Pkce(verifier, challenge, RandomText(16), RandomText(16));
    }

    public static string Base64Url(ReadOnlySpan<byte> data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string RandomText(int byteCount) =>
        Base64Url(RandomNumberGenerator.GetBytes(byteCount));
}
