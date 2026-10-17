using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrystalCode.Engine.Home;

internal static class HomeJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Reads the hand-edited provider catalog. The host never rewrites an
    /// existing providers.json, so comments survive; other Home JSON files
    /// stay strict because the host rewrites them.
    /// </summary>
    public static JsonDocumentOptions ProviderCatalogReadOptions { get; } = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Re-reads one provider entry after the catalog document is parsed.
    /// The entry's raw span still contains comments, so deserialization needs
    /// the same tolerance as the document parse.
    /// </summary>
    public static JsonSerializerOptions ProviderCatalogSerializerOptions { get; } = new(Options)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}
