namespace CrystalCode.Plugins.Hooks;

/// <summary>One tool result already present in an outbound model request.</summary>
public sealed record PluginModelToolResult : PluginModelItem
{
    public PluginModelToolResult(
        string id,
        string callId,
        string name,
        string text,
        bool success,
        IEnumerable<PluginModelImage>? images = null)
        : base(id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callId);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(text);
        CallId = callId;
        Name = name;
        Text = text;
        Success = success;
        Images = images is null ? [] : [.. images];
    }

    public string CallId { get; }

    public string Name { get; }

    public string Text { get; }

    public bool Success { get; }

    public IReadOnlyList<PluginModelImage> Images { get; }

    public override string ToString() => nameof(PluginModelToolResult);
}
