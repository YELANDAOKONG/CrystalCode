namespace CrystalCode.Engine.Events;

/// <summary>The current plain text of one plugin status field.</summary>
public sealed record StatusFieldValue(string Name, string Text)
{
    public override string ToString() => Name;
}
