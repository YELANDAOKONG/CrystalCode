namespace CrystalCode.Server.Hosting;

/// <summary>
/// Why the server is stopping. Reported in <c>server.state</c> (closing) and
/// <c>server.closing</c>, and on the operator's terminal.
/// </summary>
public static class ServerStopReason
{
    /// <summary>Ctrl+C in the terminal that started the server.</summary>
    public const string Interrupt = "interrupt";

    public const string Terminate = "terminate";

    public const string QuitSignal = "quit_signal";

    /// <summary><c>POST /shutdown</c>.</summary>
    public const string Request = "request";

    /// <summary>The <c>/quit</c> command.</summary>
    public const string Quit = "quit";

    /// <summary>The wrapper closed stdin under <c>--exit-on-stdin-close</c>.</summary>
    public const string StdinClosed = "stdin_closed";

    /// <summary>The host cancelled the server without saying why.</summary>
    public const string Cancelled = "cancelled";

    /// <summary>Operator-facing wording for a reason.</summary>
    public static string Describe(string reason) => reason switch
    {
        Interrupt => "Ctrl+C",
        Terminate => "SIGTERM",
        QuitSignal => "SIGQUIT",
        Request => "POST /shutdown",
        Quit => "/quit",
        StdinClosed => "stdin closed",
        Cancelled => "cancelled",
        _ => reason
    };
}
