namespace CrystalCode.Plugins.Hooks;

/// <summary>
/// The instruction block a prompt hook may extend. The host appends returned
/// text and does not replace Work, Plan, or Review.
/// </summary>
public sealed record PluginPrompt
{
    public PluginPrompt(string mode, string instructions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mode);
        ArgumentNullException.ThrowIfNull(instructions);
        Mode = mode.Trim();
        Instructions = instructions;
    }

    public string Mode { get; }

    public string Instructions { get; }

    public override string ToString() => nameof(PluginPrompt);
}
