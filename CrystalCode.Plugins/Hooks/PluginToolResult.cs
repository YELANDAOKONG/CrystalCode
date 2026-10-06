namespace CrystalCode.Plugins.Hooks;

/// <summary>Text and optional images a tool-result hook may substitute.</summary>
public sealed record PluginToolResult
{
    public PluginToolResult(string text, bool success, IEnumerable<PluginImage>? images = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
        Success = success;
        Images = images is null ? [] : [.. images];
    }

    public string Text { get; }

    public bool Success { get; }

    public IReadOnlyList<PluginImage> Images { get; }

    public override string ToString() => nameof(PluginToolResult);
}
