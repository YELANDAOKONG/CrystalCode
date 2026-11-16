using Spectre.Console.Cli;

using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Home;
using CrystalCode.Server.Hosting;

namespace CrystalCode.Commands;

/// <summary>Options for <c>crystal server</c>.</summary>
public sealed class ServerSettings : ServerCommandSettings
{
    [CommandOption("--api-base <PATH>")]
    public string? ApiBase { get; init; }

    internal override string DefaultAuth(HarnessSettings configured) => configured.Server.Auth;

    internal override bool TryResolve(
        HarnessSettings configured,
        CrystalHome home,
        out string? apiBase,
        out WebOptions? web,
        out string error)
    {
        apiBase = ApiBase ?? configured.Server.ApiBase;
        web = null;
        error = string.Empty;
        return true;
    }
}
