using Spectre.Console.Cli;

using CrystalCode.Commands;

using Xunit;

namespace CrystalCode.Tests.Commands;

public sealed class BridgeDebugTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "crystal-bridge-debug-" + Guid.NewGuid().ToString("N"));

    public BridgeDebugTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void NoFlag_OpensNoLog()
    {
        Assert.True(BridgeCommand.TryOpenDebug(new BridgeSettings(), new StringWriter(), out var debug, out _));

        Assert.Null(debug);
    }

    [Fact]
    public void BareFlag_UsesStderr()
    {
        var error = new StringWriter();
        var settings = new BridgeSettings { Debug = new FlagValue<string?> { IsSet = true } };

        Assert.True(BridgeCommand.TryOpenDebug(settings, error, out var debug, out _));

        Assert.Same(error, debug);
    }

    [Fact]
    public void NamedFile_IsAppendedTo()
    {
        var path = Path.Combine(_directory, "bridge.log");
        File.WriteAllText(path, "before\n");
        var settings = new BridgeSettings { Debug = new FlagValue<string?> { IsSet = true, Value = path } };

        Assert.True(BridgeCommand.TryOpenDebug(settings, new StringWriter(), out var debug, out _));
        debug!.WriteLine("after");
        debug.Dispose();

        Assert.Equal("before\nafter\n", File.ReadAllText(path).Replace("\r\n", "\n"));
    }

    [Fact]
    public void UnwritablePath_IsRefusedWithAMessage()
    {
        var path = Path.Combine(_directory, "missing", "bridge.log");
        var settings = new BridgeSettings { Debug = new FlagValue<string?> { IsSet = true, Value = path } };

        Assert.False(BridgeCommand.TryOpenDebug(settings, new StringWriter(), out var debug, out var message));

        Assert.Null(debug);
        Assert.StartsWith("Could not open the debug log", message, StringComparison.Ordinal);
    }
}
