namespace CrystalCode.Engine.Configuration;

/// <summary>
/// Status-bar label for one set service tier or speed. An unset slot is blank.
/// </summary>
public static class CatalogSlotStatus
{
    public static string For(string label, string? token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        return string.IsNullOrEmpty(token) ? string.Empty : label + " " + DisplayCase.Token(token);
    }
}
