using System.Runtime.InteropServices;

using CrystalCode.Engine.Version;

using Xunit;

namespace CrystalCode.Engine.Tests.Version;

public sealed class HostOperatingSystemTests
{
    [Fact]
    public void Describe_KeepsTheRuntimeDescription()
    {
        var operatingSystem = HostOperatingSystem.Describe("KDE neon User Edition", Architecture.X64);

        Assert.Equal("KDE neon User Edition x64", operatingSystem);
    }

    [Fact]
    public void Describe_KeepsWindowsDescription()
    {
        var operatingSystem = HostOperatingSystem.Describe(
            "Microsoft Windows 10.0.26100",
            Architecture.Arm64);

        Assert.Equal("Microsoft Windows 10.0.26100 arm64", operatingSystem);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Describe_OmitsBlankDescription(string? description)
    {
        Assert.Null(HostOperatingSystem.Describe(description, Architecture.X64));
    }

    [Fact]
    public void Describe_OmitsArchitectureWhenItIsMissing()
    {
        var operatingSystem = HostOperatingSystem.Describe("  Ubuntu 24.04.2 LTS  ", null);

        Assert.Equal("Ubuntu 24.04.2 LTS", operatingSystem);
    }
}
