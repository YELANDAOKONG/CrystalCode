using System.Security.Cryptography;
using System.Text;

namespace CrystalCode.Authentication.ChatGpt;

/// <summary>
/// The stable identifier this host sends so the same machine and user are
/// recognized across accounts. It is a name-based GUID of the machine and user
/// names, so nothing has to be stored and the names themselves are not sent.
/// </summary>
internal static class HostIdentity
{
    public static string Create() =>
        Create(Environment.MachineName, Environment.UserName);

    public static string Create(string machine, string user)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"crystal-code|{machine}|{user}"));
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return "urn:uuid:" + new Guid(bytes, bigEndian: true);
    }
}
