using Spectre.Console.Cli;

using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Home;
using CrystalCode.Server.Hosting;

namespace CrystalCode.Commands;

/// <summary>
/// Options for <c>crystal web</c>. It takes every <c>crystal server</c> option
/// except <c>--api-base</c>, because the API prefix is fixed. The front end
/// comes from <c>web/</c> in the Crystal home.
/// </summary>
public sealed class WebSettings : ServerCommandSettings
{
    [CommandOption("--spa-fallback <on|off>")]
    public string? SpaFallback { get; init; }

    internal override bool TryResolve(
        HarnessSettings configured,
        CrystalHome home,
        out string? apiBase,
        out WebOptions? web,
        out string error)
    {
        apiBase = null;
        web = null;
        error = string.Empty;
        var spaFallback = configured.Web.SpaFallback;
        if (SpaFallback is { } text)
        {
            switch (text.Trim().ToLowerInvariant())
            {
                case "on":
                    spaFallback = true;
                    break;
                case "off":
                    spaFallback = false;
                    break;
                default:
                    error = "--spa-fallback must be on or off.";
                    return false;
            }
        }

        web = new WebOptions { Root = home.WebDirectory, SpaFallback = spaFallback };
        return true;
    }
}
