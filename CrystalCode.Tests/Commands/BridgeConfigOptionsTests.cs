using CrystalCode.Commands;
using CrystalCode.Engine.Configuration;

using Xunit;

namespace CrystalCode.Tests.Commands;

public sealed class BridgeConfigOptionsTests : IDisposable
{
    private readonly string _home = Path.Combine(
        Path.GetTempPath(),
        "crystal-bridge-options-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }

    [Fact]
    public void NoFlagAndNoCustomConfig_IsTheBuiltInDefault()
    {
        Assert.True(Resolve(new BridgeSettings { Home = _home }, out var options, out _));

        Assert.Equal(BridgeHostSettings.DefaultOptions, options);
    }

    [Fact]
    public void CustomConfig_AppliesWhenNoFlagIsGiven()
    {
        WriteConfig("""{ "bridge": { "customConfigOptions": true, "configOptions": ["model", "approval"] } }""");

        Assert.True(Resolve(new BridgeSettings { Home = _home }, out var options, out _));

        Assert.Equal(["model", "approval"], options);
    }

    [Fact]
    public void CustomConfigOff_IgnoresTheSavedList()
    {
        WriteConfig("""{ "bridge": { "configOptions": ["model"] } }""");

        Assert.True(Resolve(new BridgeSettings { Home = _home }, out var options, out _));

        Assert.Equal(BridgeHostSettings.DefaultOptions, options);
    }

    [Fact]
    public void ConfigOptionsFlag_BeatsACustomConfig()
    {
        WriteConfig("""{ "bridge": { "customConfigOptions": true, "configOptions": ["model"] } }""");

        Assert.True(Resolve(new BridgeSettings { Home = _home, ConfigOptions = "mode,agents" }, out var options, out _));

        Assert.Equal(["mode", "agents"], options);
    }

    [Fact]
    public void ConfigOptionsFlag_AcceptsAll()
    {
        Assert.True(Resolve(new BridgeSettings { Home = _home, ConfigOptions = "all" }, out var options, out _));

        Assert.Equal(BridgeHostSettings.AvailableOptions, options);
    }

    [Fact]
    public void AllFlag_ShowsEveryOptionEvenWhenConfigIsCustom()
    {
        WriteConfig("""{ "bridge": { "customConfigOptions": true, "configOptions": ["model"] } }""");

        Assert.True(Resolve(new BridgeSettings { Home = _home, AllConfigOptions = true }, out var options, out _));

        Assert.Equal(BridgeHostSettings.AvailableOptions, options);
    }

    [Fact]
    public void DefaultFlag_ForcesTheBuiltInListEvenWhenConfigIsCustom()
    {
        WriteConfig("""{ "bridge": { "customConfigOptions": true, "configOptions": ["all"] } }""");

        Assert.True(Resolve(new BridgeSettings { Home = _home, DefaultConfigOptions = true }, out var options, out _));

        Assert.Equal(BridgeHostSettings.DefaultOptions, options);
    }

    [Fact]
    public void FlagsDoNotNeedAReadableConfig()
    {
        WriteConfig("{ not json");

        Assert.True(Resolve(new BridgeSettings { Home = _home, AllConfigOptions = true }, out _, out _));
        Assert.False(Resolve(new BridgeSettings { Home = _home }, out _, out var error));
        Assert.Contains("config.json", error, StringComparison.Ordinal);
    }

    [Fact]
    public void FlagsDoNotWriteConfig()
    {
        Assert.True(Resolve(new BridgeSettings { Home = _home, ConfigOptions = "mode" }, out _, out _));
        Assert.True(Resolve(new BridgeSettings { Home = _home }, out _, out _));

        Assert.DoesNotContain("bridge", File.ReadAllText(Path.Combine(_home, "config.json")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("mode", true, false)]
    [InlineData("mode", false, true)]
    [InlineData(null, true, true)]
    public void MoreThanOneFlag_IsRefused(string? list, bool useDefault, bool useAll)
    {
        var settings = new BridgeSettings
        {
            Home = _home,
            ConfigOptions = list,
            DefaultConfigOptions = useDefault,
            AllConfigOptions = useAll
        };

        Assert.False(Resolve(settings, out _, out var error));
        Assert.Contains("only one", error, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownId_IsRefusedAndNamed()
    {
        Assert.False(Resolve(new BridgeSettings { Home = _home, ConfigOptions = "mode,nope" }, out _, out var error));

        Assert.Contains("nope", error, StringComparison.Ordinal);
    }

    private static bool Resolve(BridgeSettings settings, out IReadOnlyList<string> options, out string error) =>
        BridgeCommand.TryConfigOptions(settings, out options, out error);

    private void WriteConfig(string json)
    {
        Directory.CreateDirectory(_home);
        File.WriteAllText(Path.Combine(_home, "config.json"), json);
    }
}
