using CrystalCode.Commands;

using Xunit;

namespace CrystalCode.Tests.Commands;

public sealed class BridgeWorkspaceTrustTests : IDisposable
{
    private readonly string _home = Path.Combine(
        Path.GetTempPath(),
        "crystal-bridge-trust-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }

    [Fact]
    public void Default_IsOff()
    {
        Assert.True(BridgeCommand.TryWorkspaceTrust(new BridgeSettings { Home = _home }, out var enabled, out _));

        Assert.False(enabled);
    }

    [Fact]
    public void TerminalWorkspaceTrust_DoesNotApplyToBridge()
    {
        Directory.CreateDirectory(_home);
        File.WriteAllText(Path.Combine(_home, "config.json"), "{}");

        Assert.True(BridgeCommand.TryWorkspaceTrust(new BridgeSettings { Home = _home }, out var enabled, out _));

        Assert.False(enabled);
    }

    [Fact]
    public void ConfigSwitch_TurnsItOn()
    {
        Assert.True(BridgeCommand.TryWorkspaceTrust(new BridgeSettings { Home = _home }, out _, out _));
        var path = Path.Combine(_home, "config.json");
        File.WriteAllText(path, File.ReadAllText(path).Replace("{", "{ \"bridge\": { \"workspaceTrust\": true },", StringComparison.Ordinal));

        Assert.True(BridgeCommand.TryWorkspaceTrust(new BridgeSettings { Home = _home }, out var enabled, out _));

        Assert.True(enabled);
    }

    [Fact]
    public void Flag_WinsOverConfig()
    {
        Assert.True(BridgeCommand.TryWorkspaceTrust(new BridgeSettings { Home = _home, WorkspaceTrust = "on" }, out var on, out _));
        Assert.True(BridgeCommand.TryWorkspaceTrust(new BridgeSettings { Home = _home, WorkspaceTrust = "off" }, out var off, out _));

        Assert.True(on);
        Assert.False(off);
    }

    [Fact]
    public void BadFlagValue_IsRefused()
    {
        Assert.False(BridgeCommand.TryWorkspaceTrust(new BridgeSettings { Home = _home, WorkspaceTrust = "maybe" }, out _, out var error));

        Assert.NotEmpty(error);
    }
}
