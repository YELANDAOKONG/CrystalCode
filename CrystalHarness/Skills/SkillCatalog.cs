namespace CrystalHarness.Skills;

/// <summary>
/// Skills discovered for one workspace. Lookup is case-sensitive by name.
/// </summary>
public sealed class SkillCatalog
{
    private readonly IReadOnlyDictionary<string, SkillInfo> _byName;

    public SkillCatalog(IEnumerable<SkillInfo> skills)
    {
        ArgumentNullException.ThrowIfNull(skills);
        var byName = new Dictionary<string, SkillInfo>(StringComparer.Ordinal);
        foreach (var skill in skills)
        {
            ArgumentNullException.ThrowIfNull(skill);
            byName[skill.Name] = skill;
        }

        _byName = byName;
        Items = [.. byName.Values.OrderBy(skill => skill.Name, StringComparer.Ordinal)];
    }

    public static SkillCatalog Empty { get; } = new([]);

    public IReadOnlyList<SkillInfo> Items { get; }

    public int Count => Items.Count;

    public SkillInfo? Find(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _byName.GetValueOrDefault(name.Trim());
    }
}
