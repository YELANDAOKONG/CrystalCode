using System.Reflection;

using Crystal.Chat;

using CrystalCode.Engine.Version;

using Xunit;

namespace CrystalCode.Engine.Tests.Version;

public sealed class BuildIdentityReaderTests
{
    [Fact]
    public void Read_ReportsCommitsAndCompilingSdk()
    {
        var identity = BuildIdentityReader.Read(
            typeof(BuildIdentityReader).Assembly,
            ".NET 10.0.4");

        Assert.Matches("^[0-9a-fA-F]{40}$", identity.ProductRevision);
        Assert.Matches("^[0-9a-fA-F]{40}$", identity.LibraryRevision);
        Assert.False(string.IsNullOrWhiteSpace(identity.SdkVersion));
        Assert.DoesNotContain(' ', identity.SdkVersion);
        Assert.Equal(".NET 10.0.4", identity.Runtime);
        Assert.DoesNotContain("1.0.0", identity.ProductRevision, StringComparison.Ordinal);
        Assert.DoesNotContain("1.0.0", identity.LibraryRevision, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_UsesTheRequestedLibraryAssembly()
    {
        var product = typeof(BuildIdentityReader).Assembly;
        var identity = BuildIdentityReader.Read(product, typeof(ChatMessage).Assembly, "  ");

        Assert.Equal(
            SourceRevision.FromInformationalVersion(InformationalVersion(typeof(ChatMessage).Assembly)),
            identity.LibraryRevision);
        Assert.Null(identity.Runtime);
        Assert.Equal(SdkMetadata(product), identity.SdkVersion);
    }

    private static string? InformationalVersion(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    private static string? SdkMetadata(Assembly assembly)
    {
        foreach (var attribute in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (string.Equals(attribute.Key, BuildIdentityReader.SdkMetadataKey, StringComparison.Ordinal))
            {
                return attribute.Value;
            }
        }

        return null;
    }
}
