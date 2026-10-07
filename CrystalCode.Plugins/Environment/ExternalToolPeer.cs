namespace CrystalCode.Plugins.Environment;

/// <summary>One external tool that loaded into the model catalog.</summary>
public sealed record ExternalToolPeer
{
    public ExternalToolPeer(string name, string set, string source, bool plan, bool work)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(set);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (source is not ("home" or "project"))
        {
            throw new ArgumentException("Source must be home or project.", nameof(source));
        }

        if (!plan && !work)
        {
            throw new ArgumentException(
                "An external tool must belong to Plan, Work, or both.",
                nameof(plan));
        }

        Name = name;
        Set = set;
        Source = source;
        Plan = plan;
        Work = work;
    }

    public string Name { get; }

    public string Set { get; }

    public string Source { get; }

    public bool Plan { get; }

    public bool Work { get; }

    public override string ToString() => Name;
}
