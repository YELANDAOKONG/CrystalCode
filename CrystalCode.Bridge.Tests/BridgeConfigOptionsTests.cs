using System.Text.Json;

using CrystalCode.Bridge.Tests.Support;
using CrystalCode.Engine.Configuration;

using Xunit;

namespace CrystalCode.Bridge.Tests;

/// <summary>Which session options a client sees as config options.</summary>
public sealed class BridgeConfigOptionsTests
{
    private static readonly string[] BuiltIn = ["mode", "model", "thinking", "approval", "agents"];

    [Fact]
    public async Task ByDefault_OnlyTheBuiltInOptionsAreOffered()
    {
        await using var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false));
        await fixture.RequestAsync("initialize", new { protocolVersion = 1 });

        var created = await fixture.RequestAsync("session/new", New(fixture));

        var ids = Ids(created.GetProperty("result").GetProperty("configOptions"));
        Assert.Equal(BuiltIn, ids);
        Assert.True(created.GetProperty("result").TryGetProperty("modes", out _));
    }

    [Fact]
    public async Task AllOptions_OffersTheWholeCatalog()
    {
        await using var fixture = await BridgeFixture.StartAsync(
            new ScriptedModel(false),
            configOptions: BridgeHostSettings.AvailableOptions);
        await fixture.RequestAsync("initialize", new { protocolVersion = 1 });

        var created = await fixture.RequestAsync("session/new", New(fixture));

        var ids = Ids(created.GetProperty("result").GetProperty("configOptions"));
        Assert.Contains("approval_model", ids);
        Assert.Contains("extended_approval", ids);
        Assert.Contains("prompt_set", ids);
        Assert.Contains("micro_compaction", ids);
        Assert.Contains("estimated_tokens", ids);
    }

    [Fact]
    public async Task AChosenList_KeepsTheCatalogOrderNotTheListOrder()
    {
        await using var fixture = await BridgeFixture.StartAsync(
            new ScriptedModel(false),
            configOptions: ["estimated_tokens", "model"]);
        await fixture.RequestAsync("initialize", new { protocolVersion = 1 });

        var created = await fixture.RequestAsync("session/new", New(fixture));

        Assert.Equal(
            ["model", "estimated_tokens"],
            Ids(created.GetProperty("result").GetProperty("configOptions")));
    }

    [Fact]
    public async Task WithoutMode_TheLegacyModesFieldIsOmitted()
    {
        await using var fixture = await BridgeFixture.StartAsync(
            new ScriptedModel(false),
            configOptions: ["model", "approval"]);
        await fixture.RequestAsync("initialize", new { protocolVersion = 1 });

        var created = await fixture.RequestAsync("session/new", New(fixture));

        Assert.False(created.GetProperty("result").TryGetProperty("modes", out _));
    }

    [Fact]
    public async Task AHiddenOption_CannotBeSetThroughAcp()
    {
        await using var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false));
        var id = await fixture.OpenAsync();

        var hidden = await fixture.RequestAsync(
            "session/set_config_option",
            new { sessionId = id, configId = "estimated_tokens", value = "on" });

        Assert.Equal(-32602, hidden.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task AHiddenModeSwitch_IsRefusedByTheLegacySetMode()
    {
        await using var fixture = await BridgeFixture.StartAsync(
            new ScriptedModel(false),
            configOptions: ["model"]);
        var id = await fixture.OpenAsync();

        var refused = await fixture.RequestAsync("session/set_mode", new { sessionId = id, modeId = "plan" });

        Assert.Equal(-32602, refused.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task AHiddenOptionStaysAvailableAsASlashCommand()
    {
        await using var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false));
        var id = await fixture.OpenAsync();

        var prompt = await fixture.RequestAsync(
            "session/prompt",
            new { sessionId = id, prompt = new[] { new { type = "text", text = "/tokens on" } } });

        Assert.True(prompt.TryGetProperty("result", out _));
        var config = File.ReadAllText(Path.Combine(fixture.Home, "config.json")).Replace(" ", string.Empty);
        Assert.Contains("\"estimatedTokens\":true", config, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetConfigOption_ReturnsOnlyTheOfferedOptions()
    {
        await using var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false));
        var id = await fixture.OpenAsync();

        var set = await fixture.RequestAsync(
            "session/set_config_option",
            new { sessionId = id, configId = "approval", value = "edit" });

        Assert.Equal(BuiltIn, Ids(set.GetProperty("result").GetProperty("configOptions")));
    }

    [Fact]
    public async Task ConfigOptionUpdates_AreFilteredLikeTheResponses()
    {
        await using var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false));
        var id = await fixture.OpenAsync();

        await fixture.RequestAsync(
            "session/set_config_option",
            new { sessionId = id, configId = "approval", value = "edit" });
        var update = await fixture.WaitForUpdateAsync(
            "config_option_update",
            static update => update.GetProperty("configOptions").EnumerateArray().Any(
                static option => option.GetProperty("id").GetString() == "approval"
                    && option.GetProperty("currentValue").GetString() == "edit"));

        Assert.Equal(
            BuiltIn,
            Ids(update.GetProperty("params").GetProperty("update").GetProperty("configOptions")));
    }

    private static object New(BridgeFixture fixture) =>
        new { cwd = fixture.Workspace, mcpServers = Array.Empty<object>() };

    private static string[] Ids(JsonElement options) =>
        [.. options.EnumerateArray().Select(static option => option.GetProperty("id").GetString()!)];
}
