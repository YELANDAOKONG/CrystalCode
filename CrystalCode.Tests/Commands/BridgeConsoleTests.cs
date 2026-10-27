using Spectre.Console;

using CrystalCode.Commands;

using Xunit;

namespace CrystalCode.Tests.Commands;

[Collection(ConsoleStateCollection.Name)]
public sealed class BridgeConsoleTests
{
    [Fact]
    public void ProtectStdout_PointsConsoleOutputAtStderr()
    {
        var previousOut = Console.Out;
        var previousError = Console.Error;
        var previousConsole = AnsiConsole.Console;
        var stderr = new StringWriter();
        try
        {
            Console.SetError(stderr);
            BridgeCommand.ProtectStdout();
            Console.WriteLine("plugin noise");
            AnsiConsole.WriteLine("spectre noise");

            Assert.Contains("plugin noise", stderr.ToString(), StringComparison.Ordinal);
            Assert.Contains("spectre noise", stderr.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
            AnsiConsole.Console = previousConsole;
        }
    }
}
