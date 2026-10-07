using System.Reflection;
using System.Runtime.InteropServices;

using Crystal.Chat;

namespace CrystalCode.Engine.Version;

/// <summary>
/// Reads the product commit, the Crystal library commit, the compiling SDK,
/// and the compile configuration. The host supplies the runtime and operating
/// system. The Crystal type below only identifies that library's assembly.
/// </summary>
public static class BuildIdentityReader
{
    public const string SdkMetadataKey = "NETCoreSdkVersion";

    public static BuildIdentity Read(Assembly product, string? runtime)
    {
        ArgumentNullException.ThrowIfNull(product);
        return Read(product, runtime, operatingSystem: null, architecture: null);
    }

    public static BuildIdentity Read(
        Assembly product,
        string? runtime,
        string? operatingSystem,
        Architecture? architecture)
    {
        ArgumentNullException.ThrowIfNull(product);
        return Read(product, typeof(ChatMessage).Assembly, runtime, operatingSystem, architecture);
    }

    public static BuildIdentity Read(Assembly product, Assembly library, string? runtime)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(library);
        return Read(product, library, runtime, operatingSystem: null, architecture: null);
    }

    public static BuildIdentity Read(
        Assembly product,
        Assembly library,
        string? runtime,
        string? operatingSystem,
        Architecture? architecture)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(library);
        return new BuildIdentity(
            SourceRevision.FromInformationalVersion(InformationalVersion(product)),
            SourceRevision.FromInformationalVersion(InformationalVersion(library)),
            SdkVersion(product),
            Configuration(product),
            string.IsNullOrWhiteSpace(runtime) ? null : runtime.Trim(),
            HostOperatingSystem.Describe(operatingSystem, architecture));
    }

    private static string? InformationalVersion(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    private static string? SdkVersion(Assembly assembly)
    {
        foreach (var attribute in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (!string.Equals(attribute.Key, SdkMetadataKey, StringComparison.Ordinal))
            {
                continue;
            }

            return string.IsNullOrWhiteSpace(attribute.Value) ? null : attribute.Value.Trim();
        }

        return null;
    }

    private static string? Configuration(Assembly assembly)
    {
        var configuration = assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration;
        return string.IsNullOrWhiteSpace(configuration) ? null : configuration.Trim();
    }
}
