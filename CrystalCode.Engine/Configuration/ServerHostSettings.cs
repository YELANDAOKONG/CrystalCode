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
        bool allowPrivateNetwork,
        string? apiBase = null)
    {
        ArgumentNullException.ThrowIfNull(allowOrigins);
        ArgumentNullException.ThrowIfNull(allowHosts);
        ArgumentNullException.ThrowIfNull(exposeHeaders);
        foreach (var origin in allowOrigins)
        {
            if (!TryNormalizeOrigin(origin, out _))
            {
                throw new ArgumentException(
                    "server.allowOrigins entries must be * or an http or https origin such as https://app.example.com.",
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
        if (!TryNormalizeApiBase(apiBase, out var normalized))
        {
            throw new ArgumentException(
                "server.apiBase must be / or an absolute path such as /api, with no query, fragment, or empty segment.",
                nameof(apiBase));
        }

        ApiBase = normalized;
    }

    public IReadOnlyList<string> AllowOrigins { get; }

    public IReadOnlyList<string> AllowHosts { get; }

    /// <summary>Added to <see cref="DefaultExposedHeaders"/>.</summary>
    public IReadOnlyList<string> ExposeHeaders { get; }

    /// <summary>Answer the Private Network Access preflight of older browsers.</summary>
    public bool AllowPrivateNetwork { get; }

    /// <summary>
    /// The URL path every route is served under, without a trailing slash.
    /// Empty serves from the root.
    /// </summary>
    public string ApiBase { get; }

    public bool IsDefault =>
        AllowOrigins.Count == 0
        && AllowHosts.Count == 0
        && ExposeHeaders.Count == 0
        && !AllowPrivateNetwork
        && ApiBase.Length == 0;

    /// <summary>Allows a page from any origin.</summary>
    public const string AnyOrigin = "*";

    /// <summary>
    /// Normalizes an origin the way the server compares it: no path, no trailing
    /// slash. <see cref="AnyOrigin"/> is accepted as it is.
    /// </summary>
    public static bool TryNormalizeOrigin(string? text, out string origin)
    {
        origin = string.Empty;
        if (text?.Trim() == AnyOrigin)
        {
            origin = AnyOrigin;
            return true;
        }

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

    /// <summary>
    /// Normalizes the API base path: a leading slash, no trailing slash, and
    /// empty for the root. Null, empty, and <c>/</c> all mean the root.
    /// </summary>
    public static bool TryNormalizeApiBase(string? text, out string apiBase)
    {
        apiBase = string.Empty;
        var value = text?.Trim() ?? string.Empty;
        if (value.Length == 0 || value == "/")
        {
            return true;
        }

        if (value[0] != '/')
        {
            return false;
        }

        var trimmed = value.TrimEnd('/');
        foreach (var segment in trimmed[1..].Split('/'))
        {
            if (segment.Length == 0
                || segment is "." or ".."
                || segment.Any(static c => char.IsControl(c) || char.IsWhiteSpace(c) || c is '?' or '#' or '\\' or '{' or '}' or '%' or '*'))
            {
                return false;
            }
        }

        apiBase = trimmed;
        return true;
    }

    public static bool IsHeaderName(string? text) =>
        !string.IsNullOrEmpty(text)
        && text.All(static c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
}
