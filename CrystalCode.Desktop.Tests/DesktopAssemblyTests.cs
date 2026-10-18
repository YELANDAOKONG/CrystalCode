using System.Reflection;

using CrystalCode.Desktop;

using Xunit;

namespace CrystalCode.Desktop.Tests;

public sealed class DesktopAssemblyTests
{
    [Theory]
    [InlineData("CrystalCode")]
    [InlineData("CrystalCode.Display")]
    [InlineData("Spectre.Console")]
    public void Desktop_DoesNotReferenceTerminalSurface(string assemblyName)
    {
        var referenced = typeof(App).Assembly
            .GetReferencedAssemblies()
            .Select(static name => name.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(assemblyName, referenced);
    }
}
