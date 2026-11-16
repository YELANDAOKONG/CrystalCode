namespace CrystalCode.Server.Hosting;

/// <summary>
/// Turns <c>crystal server</c> into <c>crystal web</c>: the same API, fixed
/// under <see cref="ApiBase"/>, plus the operator's own front end files and the
/// <c>/config</c> route. The server contains no HTML of its own.
/// </summary>
public sealed record WebOptions
{
    /// <summary>The API prefix. It is not configurable in web mode.</summary>
    public const string ApiBase = "/api";

    /// <summary>The directory that holds the front end, normally <c>~/.crystal/web</c>.</summary>
    public required string Root { get; init; }

    /// <summary>
    /// Answer a path that has no file and no extension with <c>index.html</c>.
    /// A path with an extension is always a plain 404.
    /// </summary>
    public bool SpaFallback { get; init; } = true;
}
