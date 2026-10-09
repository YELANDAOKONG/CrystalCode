using Crystal.Tools;

using CrystalCode.Engine.Home;
using CrystalCode.Engine.Tools.External;
using CrystalCode.Tools;

namespace CrystalCode.Engine.Plugins.Disk;

/// <summary>
/// Forwards a plugin <see cref="IHostTool"/> through the host-context overload.
/// The context is captured when the call starts.
/// </summary>
internal sealed class PluginHostTool : ITool
{
    private readonly IHostTool _inner;
    private readonly SessionToolHost _host;
    private readonly string _directoryName;

    public PluginHostTool(IHostTool inner, SessionToolHost host, string directoryName)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryName);
        _inner = inner;
        _host = host;
        _directoryName = directoryName;
        Definition = inner.Definition;
    }

    public ToolDefinition Definition { get; }

    public ValueTask<ToolOutput> InvokeAsync(
        ToolCall call,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(call);
        return _inner.InvokeAsync(
            call,
            _host.CreateContext(ExtensionDataKind.Plugins, _directoryName),
            cancellationToken);
    }
}
