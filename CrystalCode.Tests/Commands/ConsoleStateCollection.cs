using Xunit;

namespace CrystalCode.Tests.Commands;

/// <summary>
/// Tests that replace process-wide console writers. They run alone, so no
/// other test writes to the console while it is redirected.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConsoleStateCollection
{
    public const string Name = "Console state";
}
