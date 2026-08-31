namespace CrystalHarness.Skills;

/// <summary>
/// One discovered skill. Content is the SKILL.md body after frontmatter.
/// </summary>
public sealed record SkillInfo
{
    public SkillInfo(string name, string description, string location, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        ArgumentNullException.ThrowIfNull(content);

        Name = name.Trim();
        Description = description.Trim();
        Location = Path.GetFullPath(location);
        Content = content.Trim();
    }

    public string Name { get; }

    public string Description { get; }

    public string Location { get; }

    public string Content { get; }

    public string Directory => Path.GetDirectoryName(Location) ?? Location;

    public override string ToString() => nameof(SkillInfo);
}
