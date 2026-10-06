using CrystalCode.Commands;
using Spectre.Console.Cli;

namespace CrystalCode;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (VersionCommand.IsVersionRequest(args))
        {
            VersionCommand.Write(Console.Out);
            return 0;
        }

        var app = new CommandApp<RunCommand>();
        app.Configure(static config =>
        {
            config.SetApplicationName("crystal");
            config.AddCommand<TaskRunCommand>("run")
                .WithDescription("Run one task without a terminal and exit.");
            config.AddCommand<VersionCommand>("version")
                .WithDescription("Print the build identity and exit. --version prints the same text.");
        });

        return await app.RunAsync(args);
    }
}
