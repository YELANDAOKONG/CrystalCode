using CrystalCode.Commands;
using Spectre.Console.Cli;

namespace CrystalCode;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var app = new CommandApp<RunCommand>();
        app.Configure(static config =>
        {
            config.SetApplicationName("crystal");
            config.AddCommand<TaskRunCommand>("run")
                .WithDescription("Run one task without a terminal and exit.");
        });

        return await app.RunAsync(args);
    }
}
