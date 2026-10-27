using CrystalCode.Engine.Sessions;

namespace CrystalCode.Server.Security;

/// <summary>
/// Parses scope names and assigns the scope a slash command needs. Every
/// <see cref="SessionVerb"/> has an entry, so a new verb fails the build's
/// parity test until it is classified.
/// </summary>
public static class ServerScopes
{
    public static string Name(ServerScope scope) => scope switch
    {
        ServerScope.Observe => "observe",
        ServerScope.Operate => "operate",
        ServerScope.Admin => "admin",
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Scope is not supported.")
    };

    public static bool TryParse(string? text, out ServerScope scope)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "observe":
                scope = ServerScope.Observe;
                return true;
            case "operate":
                scope = ServerScope.Operate;
                return true;
            case "admin":
                scope = ServerScope.Admin;
                return true;
            default:
                scope = ServerScope.Observe;
                return false;
        }
    }

    /// <summary>
    /// The scope <paramref name="input"/> needs. Plain text and reports need
    /// <see cref="ServerScope.Operate"/>; commands that change trust, access,
    /// approval, the workspace, or saved configuration need
    /// <see cref="ServerScope.Admin"/>.
    /// </summary>
    public static ServerScope ForInput(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return SessionCommand.TryParse(input.Trim(), out var command)
            ? ForVerb(command.Verb, command.Argument)
            : ServerScope.Operate;
    }

    public static ServerScope ForVerb(SessionVerb verb, string argument = "")
    {
        ArgumentNullException.ThrowIfNull(argument);
        return verb switch
        {
            SessionVerb.None => ServerScope.Operate,
            SessionVerb.Help => ServerScope.Operate,
            SessionVerb.Plan => ServerScope.Operate,
            SessionVerb.Mode => ServerScope.Operate,
            SessionVerb.Status => ServerScope.Operate,
            SessionVerb.Stats => ServerScope.Operate,
            SessionVerb.Btw => ServerScope.Operate,
            SessionVerb.Clear => ServerScope.Operate,
            SessionVerb.Fork => ServerScope.Operate,
            SessionVerb.Child => ServerScope.Operate,
            SessionVerb.Sessions => ServerScope.Operate,
            SessionVerb.Compact => IsDisplayOnly(argument) ? ServerScope.Operate : ServerScope.Admin,
            SessionVerb.Todos => argument.Trim().Length == 0 ? ServerScope.Operate : ServerScope.Admin,
            SessionVerb.Attach => ServerScope.Operate,
            SessionVerb.Quick => ServerScope.Operate,
            SessionVerb.Export => ServerScope.Operate,
            SessionVerb.Unknown => ServerScope.Operate,
            SessionVerb.Approval => ServerScope.Admin,
            SessionVerb.Thinking => ServerScope.Admin,
            SessionVerb.Context => ServerScope.Admin,
            SessionVerb.Tier => ServerScope.Admin,
            SessionVerb.Speed => ServerScope.Admin,
            SessionVerb.Tokens => ServerScope.Admin,
            SessionVerb.Model => ServerScope.Admin,
            SessionVerb.PromptSet => ServerScope.Admin,
            SessionVerb.PromptAttachment => ServerScope.Admin,
            SessionVerb.StatusLine => ServerScope.Admin,
            SessionVerb.Cd => argument.Trim().Length == 0 ? ServerScope.Operate : ServerScope.Admin,
            SessionVerb.Space => ServerScope.Admin,
            SessionVerb.Trust => ServerScope.Admin,
            SessionVerb.Rules => ServerScope.Admin,
            SessionVerb.Theme => argument.Trim().Length == 0 ? ServerScope.Operate : ServerScope.Admin,
            // on and off write config.json; test runs the operator's command on the server host.
            SessionVerb.Notify => argument.Trim().Length == 0 ? ServerScope.Operate : ServerScope.Admin,
            SessionVerb.Access => ServerScope.Admin,
            SessionVerb.Resume => ServerScope.Admin,
            SessionVerb.Agents => ServerScope.Admin,
            SessionVerb.Tools => argument.Trim().Length == 0 ? ServerScope.Operate : ServerScope.Admin,
            SessionVerb.Plugins => argument.Trim().Length == 0 ? ServerScope.Operate : ServerScope.Admin,
            SessionVerb.Mcp => IsListing(argument) ? ServerScope.Operate : ServerScope.Admin,
            SessionVerb.Verbose => ServerScope.Admin,
            SessionVerb.Quit => ServerScope.Admin,
            // A verb added to the engine must be classified here first.
            _ => throw new ArgumentOutOfRangeException(nameof(verb), verb, "The command has no server scope.")
        };
    }

    /// <summary>A bare /compact summarizes; every argument form writes config.json.</summary>
    private static bool IsDisplayOnly(string argument)
    {
        var text = argument.Trim();
        return text.Length == 0 || text.Equals("full", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsListing(string argument)
    {
        var text = argument.Trim();
        return text.Length == 0
            || text.Equals("list", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("show ", StringComparison.OrdinalIgnoreCase);
    }
}
