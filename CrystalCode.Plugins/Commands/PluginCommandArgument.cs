namespace CrystalCode.Plugins.Commands;

/// <summary>One slash-command argument completion and its optional nested choices.</summary>
public sealed record PluginCommandArgument
{
    public PluginCommandArgument(
        string name,
        string help,
        IEnumerable<string>? keys = null,
        IEnumerable<PluginCommandArgument>? arguments = null,
        bool argumentsOptional = false,
        IEnumerable<PluginCommandArgument>? argumentsAfterValue = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(help);
        Name = name.Trim();
        Help = help.Trim();
        Keys = [.. keys ?? [Name]];
        Arguments = [.. arguments ?? []];
        ArgumentsOptional = argumentsOptional;
        ArgumentsAfterValue = [.. argumentsAfterValue ?? []];
    }

    public string Name { get; }

    public string Help { get; }

    public IReadOnlyList<string> Keys { get; }

    public IReadOnlyList<PluginCommandArgument> Arguments { get; }

    public bool ArgumentsOptional { get; }

    public IReadOnlyList<PluginCommandArgument> ArgumentsAfterValue { get; }

    public override string ToString() => Name;
}
