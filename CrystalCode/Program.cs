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
            config.AddBranch<ExtensionSettings>("plugins", plugins =>
            {
                plugins.SetDescription("List, show, enable, or disable one plugin directory.");
                plugins.AddCommand<PluginsListCommand>("list")
                    .WithDescription("List plugin directories, including disabled ones.");
                plugins.AddCommand<PluginsShowCommand>("show")
                    .WithDescription("Show one plugin manifest.");
                plugins.AddCommand<PluginsEnableCommand>("enable")
                    .WithDescription("Enable one plugin directory.");
                plugins.AddCommand<PluginsDisableCommand>("disable")
                    .WithDescription("Disable one plugin directory.");
            });
            config.AddBranch<ExtensionSettings>("tools", tools =>
            {
                tools.SetDescription("List, show, enable, or disable one external tool set.");
                tools.AddCommand<ToolsListCommand>("list")
                    .WithDescription("List external tool set directories, including disabled ones.");
                tools.AddCommand<ToolsShowCommand>("show")
                    .WithDescription("Show one external tool set manifest.");
                tools.AddCommand<ToolsEnableCommand>("enable")
                    .WithDescription("Enable one external tool set directory.");
                tools.AddCommand<ToolsDisableCommand>("disable")
                    .WithDescription("Disable one external tool set directory.");
            });
            config.AddCommand<VersionCommand>("version")
                .WithDescription("Print the build identity and exit. --version prints the same text.");
        });

        return await app.RunAsync(args);
    }
}
