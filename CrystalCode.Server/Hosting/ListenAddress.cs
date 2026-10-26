using System.Globalization;
using System.Net;

namespace CrystalCode.Server.Hosting;

/// <summary>
/// A parsed <c>--listen</c> value: an IP address or <c>localhost</c>, and a
/// port. <c>0.0.0.0</c> and <c>[::]</c> listen on every interface.
/// </summary>
public sealed record ListenAddress(IPAddress Address, int Port, bool Localhost)
{
    public bool IsLoopback => Localhost || IPAddress.IsLoopback(Address);

    public bool IsAny => Address.Equals(IPAddress.Any) || Address.Equals(IPAddress.IPv6Any);

    public static bool TryParse(string? text, out ListenAddress address, out string error)
    {
        address = new ListenAddress(IPAddress.Loopback, 0, false);
        error = string.Empty;
        var value = text?.Trim() ?? string.Empty;
        var colon = value.LastIndexOf(':');
        if (colon <= 0 || colon == value.Length - 1)
        {
            error = "--listen must be host:port, for example 127.0.0.1:0 or [::1]:8443.";
            return false;
        }

        var host = value[..colon];
        if (host.StartsWith('[') && host.EndsWith(']'))
        {
            host = host[1..^1];
        }

        if (!int.TryParse(value[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            || port > IPEndPoint.MaxPort)
        {
            error = "--listen port must be 0 through 65535.";
            return false;
        }

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            address = new ListenAddress(IPAddress.Loopback, port, true);
            return true;
        }

        if (!IPAddress.TryParse(host, out var ip))
        {
            error = "--listen host must be an IP address or localhost.";
            return false;
        }

        address = new ListenAddress(ip, port, false);
        return true;
    }
}
