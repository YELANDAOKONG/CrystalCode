using System.Runtime.InteropServices;

namespace CrystalCode.Engine.Version;

/// <summary>
/// Host operating system line for <c>crystal version</c>.
/// </summary>
public static class HostOperatingSystem
{
    /// <summary>
    /// Keeps the runtime operating-system description and appends the architecture.
    /// </summary>
    public static string? Describe(string? description, Architecture? architecture)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        var name = description.Trim();
        if (architecture is null)
        {
            return name;
        }

        return $"{name} {ArchitectureToken(architecture.Value)}";
    }

    private static string ArchitectureToken(Architecture architecture) =>
        architecture.ToString().ToLowerInvariant();
}
