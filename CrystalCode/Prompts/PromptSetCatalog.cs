namespace CrystalCode.Prompts;

internal sealed class PromptSetCatalog
{
    private readonly IReadOnlyDictionary<string, PromptSetDefinition> _sets;

    public PromptSetCatalog(IReadOnlyDictionary<string, PromptSetDefinition> sets)
    {
        ArgumentNullException.ThrowIfNull(sets);
        _sets = sets;
    }

    public IReadOnlyList<string> Names => [.. _sets.Keys.Order(StringComparer.Ordinal)];

    public bool TryGet(string name, out PromptSetDefinition definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _sets.TryGetValue(name.Trim(), out definition!);
    }
}
