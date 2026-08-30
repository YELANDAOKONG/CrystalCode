using Spectre.Console.Cli;

using CrystalHarness.Commands;

namespace CrystalHarness;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var app = new CommandApp<RunCommand>();
        app.Configure(static config =>
        {
            config.SetApplicationName("crystal");
        });

        return await app.RunAsync(args);
    }
}
