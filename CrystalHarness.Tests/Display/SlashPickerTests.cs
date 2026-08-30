using CrystalHarness.Display;

using Xunit;

namespace CrystalHarness.Tests.Display;

public sealed class SlashPickerTests
{
    private static readonly SlashOption[] Options =
    [
        new("clear", "new conversation", ["clear", "new"]),
        new("resume", "latest or id", ["resume", "continue"]),
        new("quit", "exit", ["quit", "q"])
    ];

    [Fact]
    public void Create_FiltersByAliasPrefix()
    {
        var picker = SlashPicker.Create("/n", Options);

        Assert.NotNull(picker);
        Assert.Equal("clear", picker.Matches[0].Name);
        Assert.Equal("/clear ", picker.CompletedText);
    }

    [Fact]
    public void Create_HidesAfterSpace()
    {
        Assert.Null(SlashPicker.Create("/clear extra", Options));
    }

    [Fact]
    public void Move_CyclesSelection()
    {
        var picker = SlashPicker.Create("/", Options);

        Assert.NotNull(picker);
        picker = picker.Move(1);
        Assert.Equal("resume", picker.Matches[picker.Selected].Name);
    }
}
