namespace CrystalCode.Engine.Configuration;

/// <summary>
/// Operator defaults for <c>crystal web</c> that <c>crystal server</c> does not
/// have. The Host, Origin, and header lists stay in <see cref="ServerHostSettings"/>
/// and apply to both commands. A command line may override these for one
/// process and never writes them.
/// </summary>
public sealed record WebHostSettings
{
    public static WebHostSettings Default { get; } = new();

    /// <summary>Unless configured otherwise, <c>crystal web</c> serves without a token.</summary>
    public const string DefaultAuth = ServerHostSettings.AuthNone;

    public WebHostSettings(bool spaFallback = true, string? auth = null)
    {
        SpaFallback = spaFallback;
        if (!ServerHostSettings.TryNormalizeAuth(auth, DefaultAuth, out var normalized))
        {
            throw new ArgumentException("web.auth must be token or none.", nameof(auth));
        }

        Auth = normalized;
    }

    /// <summary>
    /// How <c>crystal web</c> authenticates when <c>--auth</c> is absent:
    /// <c>none</c> (the default) or <c>token</c>.
    /// </summary>
    public string Auth { get; }

    /// <summary>
    /// Answer a path with no matching file and no extension with
    /// <c>index.html</c>, so a front end router can handle it. A path with an
    /// extension is always a plain 404.
    /// </summary>
    public bool SpaFallback { get; }

    public bool IsDefault => SpaFallback && Auth == DefaultAuth;
}
