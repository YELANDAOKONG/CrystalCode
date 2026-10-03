using CrystalCode.Display.Shell;

using Xunit;

namespace CrystalCode.Display.Tests;

/// <summary>
/// Guards the dependency direction: Display knows nothing of the engine or
/// of Crystal, so any front end can adopt its pieces on their own.
/// </summary>
public sealed class DisplayAssemblyTests
{
    [Theory]
    [InlineData("Crystal")]
    [InlineData("Crystal.Tools")]
    [InlineData("Crystal.Agents")]
    [InlineData("Crystal.Harness")]
    [InlineData("CrystalCode")]
    [InlineData("CrystalCode.Engine")]
    [InlineData("CrystalCode.Providers")]
    public void Display_DoesNotReferenceProductAssembly(string assemblyName)
    {
        Assert.DoesNotContain(assemblyName, ReferencedAssemblyNames());
    }

    [Fact]
    public void Display_ReferencesTheTerminalLibraryItBuildsOn()
    {
        // Control for the guard above: proves the reference scan sees real
        // dependencies, so a passing guard is not an empty check.
        Assert.Contains("Spectre.Console", ReferencedAssemblyNames());
    }

    private static IReadOnlyList<string> ReferencedAssemblyNames() =>
        typeof(DisplayInput).Assembly
            .GetReferencedAssemblies()
            .Select(static name => name.Name ?? string.Empty)
            .ToArray();
}
