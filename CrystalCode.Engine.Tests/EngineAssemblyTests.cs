using System.Reflection;

using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests;

/// <summary>
/// Guards the dependency direction: the engine owns no terminal, and nothing
/// that draws may leak into it. A second front end reuses the engine only if
/// this stays true.
/// </summary>
public sealed class EngineAssemblyTests
{
    [Theory]
    [InlineData("Spectre.Console")]
    [InlineData("Spectre.Console.Cli")]
    [InlineData("Terminal.Gui")]
    [InlineData("CrystalCode.Display")]
    [InlineData("CrystalCode")]
    public void Engine_DoesNotReferenceFrontEndAssembly(string assemblyName)
    {
        var referenced = ReferencedAssemblyNames();

        Assert.DoesNotContain(assemblyName, referenced);
    }

    [Fact]
    public void Engine_ReferencesTheLibrariesItBuildsOn()
    {
        // Control for the guards above: proves the reference scan sees real
        // dependencies, so a passing guard is not an empty check.
        var referenced = ReferencedAssemblyNames();

        Assert.Contains("Crystal", referenced);
        Assert.Contains("CrystalCode.Providers", referenced);
    }

    [Fact]
    public void Engine_DoesNotReferenceTheConsole()
    {
        var referenced = ReferencedAssemblyNames();

        Assert.DoesNotContain("System.Console", referenced);
    }

    private static IReadOnlyList<string> ReferencedAssemblyNames() =>
        typeof(CodingSession).Assembly
            .GetReferencedAssemblies()
            .Select(static name => name.Name ?? string.Empty)
            .ToArray();
}
