using CrystalCode.Configuration;
using CrystalCode.Display.Composer;

using Xunit;

namespace CrystalCode.Tests.Configuration;

public sealed class ToolCompletionsTests
{
    [Fact]
    public void All_CompletesSourcePolicy()
    {
        SlashOption[] options =
        [
            new SlashOption("tools", "tools", ["tools"], ToolCompletions.All)
        ];

        var picker = SlashPicker.Create("/tools home a", options);

        Assert.NotNull(picker);
        Assert.Equal("/tools home author ", picker.CompletedText);
    }
}
