using System.Collections.ObjectModel;

using CrystalCode.Engine.Tools;

namespace CrystalCode.Engine.Mcp;

/// <summary>
/// One MCP server Crystal may connect to. Environment and header values stay
/// as written, so <c>${NAME}</c> references are not expanded here.
/// </summary>
public sealed record McpServerDefinition
{
    public const int DefaultToolTimeoutSeconds = 120;

    public McpServerDefinition(
        string name,
        McpServerSource source,
        McpTransportKind transport,
        string? command = null,
        IReadOnlyList<string>? arguments = null,
        IReadOnlyDictionary<string, string>? environment = null,
        string? url = null,
        IReadOnlyDictionary<string, string>? headers = null,
        bool inPlan = false,
        bool inWork = true,
        int? toolTimeoutSeconds = DefaultToolTimeoutSeconds,
        bool enabled = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!McpServerNames.IsServerName(name))
        {
            throw new ArgumentException("MCP server name is invalid.", nameof(name));
        }

        if (!inPlan && !inWork)
        {
            throw new ArgumentException(
                "An MCP server must be in the Plan catalog, the Work catalog, or both.",
                nameof(inWork));
        }

        if (!BashTool.IsSupported(toolTimeoutSeconds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(toolTimeoutSeconds),
                toolTimeoutSeconds,
                "MCP tool timeout must be a positive supported number of seconds, or unlimited.");
        }

        switch (transport)
        {
            case McpTransportKind.Stdio:
                ArgumentException.ThrowIfNullOrWhiteSpace(command);
                if (url is not null || headers is { Count: > 0 })
                {
                    throw new ArgumentException(
                        "A stdio MCP server cannot have a URL or headers.",
                        nameof(url));
                }

                break;
            case McpTransportKind.StreamableHttp:
            case McpTransportKind.Sse:
                ArgumentException.ThrowIfNullOrWhiteSpace(url);
                if (command is not null
                    || arguments is { Count: > 0 }
                    || environment is { Count: > 0 })
                {
                    throw new ArgumentException(
                        "An HTTP MCP server cannot have a command, arguments, or environment.",
                        nameof(command));
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(transport));
        }

        Name = name;
        Source = source;
        Transport = transport;
        Command = command?.Trim();
        Arguments = Array.AsReadOnly(arguments?.ToArray() ?? []);
        Environment = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(
                environment ?? new Dictionary<string, string>(),
                StringComparer.Ordinal));
        Url = url?.Trim();
        Headers = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(
                headers ?? new Dictionary<string, string>(),
                StringComparer.OrdinalIgnoreCase));
        InPlan = inPlan;
        InWork = inWork;
        ToolTimeoutSeconds = toolTimeoutSeconds;
        Enabled = enabled;
    }

    public string Name { get; }

    public McpServerSource Source { get; }

    public McpTransportKind Transport { get; }

    public string? Command { get; }

    public IReadOnlyList<string> Arguments { get; }

    public IReadOnlyDictionary<string, string> Environment { get; }

    public string? Url { get; }

    public IReadOnlyDictionary<string, string> Headers { get; }

    public bool InPlan { get; }

    public bool InWork { get; }

    /// <summary>
    /// Seconds one tool call may run, or null for no limit.
    /// </summary>
    public int? ToolTimeoutSeconds { get; }

    /// <summary>
    /// False keeps the definition on disk and listed but never connects it.
    /// </summary>
    public bool Enabled { get; }

    // Values can hold secrets, so the record never prints them.
    public override string ToString() => nameof(McpServerDefinition);
}
