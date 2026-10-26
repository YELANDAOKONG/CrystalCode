namespace CrystalCode.Engine.Configuration;

/// <summary>
/// Operator defaults for <c>crystal server</c> that browser front ends
/// depend on: which pages may call it, which Host names it answers to, and
/// which response headers a page may read. Command-line values are added to
/// these lists for one process. Nothing here is written by a command line.
/// </summary>
public sealed record ServerHostSettings
{
    /// <summary>Response headers a page can always read.</summary>
    public static IReadOnlyList<string> DefaultExposedHeaders { get; } =
        Array.AsReadOnly(["Retry-After", "Content-Disposition", "WWW-Authenticate"]);

    public static ServerHostSettings Default { get; } = new([], [], [], false);

    public ServerHostSettings(
        IReadOnlyList<string> allowOrigins,
        IReadOnlyList<string> allowHosts,
        IReadOnlyList<string> exposeHeaders,
        bool allowPrivateNetwork)
    {
        ArgumentNullException.ThrowIfNull(allowOrigins);
        ArgumentNullException.ThrowIfNull(allowHosts);
        ArgumentNullException.ThrowIfNull(exposeHeaders);
        foreach (var origin in allowOrigins)
        {
            if (!TryNormalizeOrigin(origin, out _))
            {
                throw new ArgumentException(
                    "server.allowOrigins entries must be an http or https origin such as https://app.example.com.",
                    nameof(allowOrigins));
            }
        }

        if (allowHosts.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("server.allowHosts entries cannot be empty.", nameof(allowHosts));
        }

        foreach (var header in exposeHeaders)
        {
            if (!IsHeaderName(header))
            {
                throw new ArgumentException(
                    "server.exposeHeaders entries must be HTTP header names.",
                    nameof(exposeHeaders));
            }
        }

        AllowOrigins = Array.AsReadOnly([.. allowOrigins]);
        AllowHosts = Array.AsReadOnly([.. allowHosts]);
        ExposeHeaders = Array.AsReadOnly([.. exposeHeaders]);
        AllowPrivateNetwork = allowPrivateNetwork;
    }

    public IReadOnlyList<string> AllowOrigins { get; }

    public IReadOnlyList<string> AllowHosts { get; }

    /// <summary>Added to <see cref="DefaultExposedHeaders"/>.</summary>
    public IReadOnlyList<string> ExposeHeaders { get; }

    /// <summary>Answer the Private Network Access preflight of older browsers.</summary>
    public bool AllowPrivateNetwork { get; }

    public bool IsDefault =>
        AllowOrigins.Count == 0
        && AllowHosts.Count == 0
        && ExposeHeaders.Count == 0
        && !AllowPrivateNetwork;

    /// <summary>Normalizes an origin the way the server compares it: no path, no trailing slash.</summary>
    public static bool TryNormalizeOrigin(string? text, out string origin)
    {
        origin = string.Empty;
        if (string.IsNullOrWhiteSpace(text)
            || !Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || uri.AbsolutePath != "/"
            || uri.Query.Length > 0
            || uri.Fragment.Length > 0
            || uri.UserInfo.Length > 0)
        {
            return false;
        }

        origin = uri.GetLeftPart(UriPartial.Authority);
        return true;
    }

    public static bool IsHeaderName(string? text) =>
        !string.IsNullOrEmpty(text)
        && text.All(static c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
}
