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

    public WebHostSettings(bool spaFallback = true)
    {
        SpaFallback = spaFallback;
    }

    /// <summary>
    /// Answer a path with no matching file and no extension with
    /// <c>index.html</c>, so a front end router can handle it. A path with an
    /// extension is always a plain 404.
    /// </summary>
    public bool SpaFallback { get; }

    public bool IsDefault => SpaFallback;
}
