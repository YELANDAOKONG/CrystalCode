using Spectre.Console.Cli;

using CrystalCode.Engine.Plugins;

namespace CrystalCode.Commands;

/// <summary>
/// Starts the HTTP front end together with the operator's web front end from
/// <c>web/</c> in the Crystal home. It is <c>crystal server</c> with the API
/// fixed under <c>/api</c>; see docs/server.md.
/// </summary>
public sealed class WebCommand : AsyncCommand<WebSettings>
{
    public override Task<int> ExecuteAsync(
        CommandContext context,
        WebSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return ServerCommand.RunAsync(settings, Console.In, Console.Out, Console.Error, PluginRegistry.CreateBuiltIn(), cancellationToken);
    }
}
