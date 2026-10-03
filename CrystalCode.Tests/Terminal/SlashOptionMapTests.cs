using CrystalCode.Display.Composer;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Sessions;
using CrystalCode.Terminal;

using Xunit;

namespace CrystalCode.Tests.Terminal;

public sealed class SlashOptionMapTests
{
    [Fact]
    public void From_KeepsNestedAndTrailingArguments()
    {
        var export = Assert.Single(ExportCompletions.All, option => option.Name == "markdown");

        var mapped = SlashOptionMap.From(export);

        Assert.Equal(["markdown", "md"], mapped.Keys);
        Assert.True(mapped.ArgumentsOptional);
        Assert.Equal("--system", Assert.Single(mapped.ArgumentOptions).Name);
        Assert.Equal("--system", Assert.Single(mapped.TrailingArgumentOptions).Name);
    }

    [Fact]
    public void From_ToolPolicyCompletesThroughThePicker()
    {
        var menu = new SlashCompletion("tools", "tools", ["tools"], ToolCompletions.All);

        var picker = SlashPicker.Create("/tools home a", [SlashOptionMap.From(menu)]);

        Assert.NotNull(picker);
        Assert.Equal("/tools home author ", picker.CompletedText);
    }
}
