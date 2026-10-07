namespace CrystalCode.Plugins.Environment;

/// <summary>
/// Immutable catalog snapshot. Lists are copied so a caller cannot change
/// what a plugin observes.
/// </summary>
public sealed class PluginEnvironment : IPluginEnvironment
{
    public static PluginEnvironment Empty { get; } = new([], [], [], []);

    public PluginEnvironment(
        IEnumerable<PluginPeer> plugins,
        IEnumerable<ExternalToolSetPeer> toolSets,
        IEnumerable<ExternalToolPeer> externalTools,
        IEnumerable<SkillPeer> skills)
    {
        ArgumentNullException.ThrowIfNull(plugins);
        ArgumentNullException.ThrowIfNull(toolSets);
        ArgumentNullException.ThrowIfNull(externalTools);
        ArgumentNullException.ThrowIfNull(skills);
        Plugins = Copy(plugins);
        ToolSets = Copy(toolSets);
        ExternalTools = Copy(externalTools);
        Skills = Copy(skills);
    }

    public IReadOnlyList<PluginPeer> Plugins { get; }

    public IReadOnlyList<ExternalToolSetPeer> ToolSets { get; }

    public IReadOnlyList<ExternalToolPeer> ExternalTools { get; }

    public IReadOnlyList<SkillPeer> Skills { get; }

    public override string ToString() => nameof(PluginEnvironment);

    private static IReadOnlyList<T> Copy<T>(IEnumerable<T> values)
    {
        var copy = new List<T>();
        foreach (var value in values)
        {
            ArgumentNullException.ThrowIfNull(value);
            copy.Add(value);
        }

        return copy;
    }
}
