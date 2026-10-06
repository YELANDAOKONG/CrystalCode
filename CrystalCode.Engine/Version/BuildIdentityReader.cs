using System.Reflection;

using Crystal.Chat;

namespace CrystalCode.Engine.Version;

/// <summary>
/// Reads the product commit, the Crystal library commit, and the compiling SDK.
/// The Crystal type below only identifies that library's assembly.
/// </summary>
public static class BuildIdentityReader
{
    public const string SdkMetadataKey = "NETCoreSdkVersion";

    public static BuildIdentity Read(Assembly product, string? runtime)
    {
        ArgumentNullException.ThrowIfNull(product);
        return Read(product, typeof(ChatMessage).Assembly, runtime);
    }

    public static BuildIdentity Read(Assembly product, Assembly library, string? runtime)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(library);
        return new BuildIdentity(
            SourceRevision.FromInformationalVersion(InformationalVersion(product)),
            SourceRevision.FromInformationalVersion(InformationalVersion(library)),
            SdkVersion(product),
            string.IsNullOrWhiteSpace(runtime) ? null : runtime.Trim());
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
}
