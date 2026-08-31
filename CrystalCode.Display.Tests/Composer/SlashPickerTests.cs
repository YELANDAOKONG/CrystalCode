using CrystalCode.Display.Composer;

using Xunit;

namespace CrystalCode.Display.Tests.Composer;

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

    [Fact]
    public void Create_CompletesNestedModelAfterProvider()
    {
        var picker = SlashPicker.Create("/model openai ", ModelOptions());

        Assert.NotNull(picker);
        Assert.Equal("gpt-5.6-sol", picker.Matches[0].Name);
        Assert.Equal("/model openai gpt-5.6-sol ", picker.CompletedText);
    }

    [Fact]
    public void Create_FiltersNestedModelPrefix()
    {
        var picker = SlashPicker.Create("/model openai gpt-5.6-t", ModelOptions());

        Assert.NotNull(picker);
        Assert.Equal("gpt-5.6-terra", picker.Matches[0].Name);
        Assert.Equal("/model openai gpt-5.6-terra ", picker.CompletedText);
    }

    [Fact]
    public void Create_HidesAfterThirdModelToken()
    {
        Assert.Null(SlashPicker.Create("/model openai gpt-5.6-sol extra", ModelOptions()));
    }

    [Fact]
    public void Create_PrefersNestedProviderWhenNameMatchesAModel()
    {
        var picker = SlashPicker.Create("/model openai gpt", ModelOptions());

        Assert.NotNull(picker);
        Assert.All(picker.Matches, match => Assert.StartsWith("gpt-5.6-", match.Name, StringComparison.Ordinal));
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

    private static SlashOption[] ModelOptions()
    {
        var model = new SlashOption(
            "model",
            "show or set provider and model",
            ["model"],
            [
                new("openai", "a model named openai", ["openai"]),
                new(
                    "openai",
                    "provider",
                    ["openai"],
                    [
                        new("gpt-5.6-sol", "openai", ["gpt-5.6-sol"]),
                        new("gpt-5.6-terra", "openai", ["gpt-5.6-terra"]),
                        new("gpt-5.6-luna", "openai", ["gpt-5.6-luna"])
                    ])
            ]);
        return [model, .. Options];
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
