using CrystalCode.Engine.Configuration;

namespace CrystalCode.Engine.Sessions;

/// <summary>
/// One <c>/rules</c> assignment. A null side keeps that side's current value.
/// </summary>
public sealed record RuleFileChange(bool? Global, bool? Project)
{
    public RuleFileSettings Apply(RuleFileSettings current)
    {
        ArgumentNullException.ThrowIfNull(current);
        return new RuleFileSettings(Global ?? current.Global, Project ?? current.Project);
    }
}
