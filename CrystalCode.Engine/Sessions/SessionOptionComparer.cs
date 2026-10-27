using CrystalCode.Engine.Events;

namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Compares options by value, including their value lists, so an unchanged
/// catalog is not published twice.
/// </summary>
internal sealed class SessionOptionComparer : IEqualityComparer<SessionOption>
{
    public static SessionOptionComparer Instance { get; } = new();

    public bool Equals(SessionOption? x, SessionOption? y) =>
        ReferenceEquals(x, y)
        || (x is not null
            && y is not null
            && x with { Values = [] } == y with { Values = [] }
            && x.Values.SequenceEqual(y.Values));

    public int GetHashCode(SessionOption obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        return HashCode.Combine(obj.Id, obj.Current, obj.Values.Count);
    }
}
