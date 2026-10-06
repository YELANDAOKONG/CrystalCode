namespace CrystalCode.Plugins.Hooks;

/// <summary>One tool call already present in an outbound model request.</summary>
public sealed record PluginModelToolCall : PluginModelItem
{
    public PluginModelToolCall(string id, string callId, string name, string arguments)
        : base(id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(arguments);
        CallId = callId;
        Name = name;
        Arguments = arguments;
    }

    public string CallId { get; }

    public string Name { get; }

    public string Arguments { get; }

    public override string ToString() => nameof(PluginModelToolCall);
}
