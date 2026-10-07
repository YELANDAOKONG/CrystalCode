namespace CrystalCode.Plugins.Environment;

/// <summary>
/// One skill currently available to the <c>skill</c> tool. Later discoveries
/// of the same name are already collapsed.
/// </summary>
public sealed record SkillPeer
{
    public SkillPeer(string name, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        Name = name.Trim();
        Description = description.Trim();
    }

    public string Name { get; }

    public string Description { get; }

    public override string ToString() => Name;
}
