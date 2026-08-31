using CrystalHarness.Display.Composer;

using Xunit;

namespace CrystalHarness.Tests.Display.Composer;

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
    public void Create_HidesAfterSpaceWhenCommandHasNoArguments()
    {
        Assert.Null(SlashPicker.Create("/clear extra", Options));
    }

    [Fact]
    public void Create_CompletesThinkingArgumentsAfterSpace()
    {
        var options = ThinkingOptions();
        var picker = SlashPicker.Create("/think ", options);

        Assert.NotNull(picker);
        Assert.Equal("off", picker.Matches[0].Name);
        Assert.Equal("/think off ", picker.CompletedText);
    }

    [Fact]
    public void Create_FiltersThinkingArgumentPrefix()
    {
        var picker = SlashPicker.Create("/think h", ThinkingOptions());

        Assert.NotNull(picker);
        Assert.Equal("high", picker.Matches[0].Name);
        Assert.Equal("/think high ", picker.CompletedText);
    }

    [Fact]
    public void Create_HidesAfterSecondArgumentSpace()
    {
        Assert.Null(SlashPicker.Create("/think high extra", ThinkingOptions()));
    }

    private static SlashOption[] ThinkingOptions()
    {
        var thinking = new SlashOption(
            "thinking",
            "off | default | high",
            ["thinking", "think"],
            [
                new("off", "disable thinking", ["off", "none"]),
                new("default", "provider default", ["default"]),
                new("high", "High", ["high"])
            ]);
        return [thinking, .. Options];
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
