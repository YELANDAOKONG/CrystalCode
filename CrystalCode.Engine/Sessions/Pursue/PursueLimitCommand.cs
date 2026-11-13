using System.Globalization;

using CrystalCode.Engine.Configuration;

namespace CrystalCode.Engine.Sessions.Pursue;

/// <summary>Validates a structured limit edit before routing it through the shared command path.</summary>
public static class PursueLimitCommand
{
    public static bool TryCreate(string name, int? value, out string command, out string error)
    {
        command = string.Empty;
        error = string.Empty;
        try
        {
            _ = PursueLimits.Default.With(name, value);
        }
        catch (ArgumentException exception)
        {
            error = exception.Message;
            return false;
        }

        command = "/pursue limit " + name + " " + (value?.ToString(CultureInfo.InvariantCulture) ?? "unlimited");
        return true;
    }
}
