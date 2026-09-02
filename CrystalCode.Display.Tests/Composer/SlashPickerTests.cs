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

    [Fact]
    public void Create_CompletesExportFormatAndOptionalSystemFlag()
    {
        var options = ExportOptions();
        var formats = SlashPicker.Create("/export ", options);
        var flag = SlashPicker.Create("/export markdown ", options);

        Assert.NotNull(formats);
        Assert.Equal(["markdown", "json"], formats.Matches.Select(match => match.Name));
        Assert.NotNull(flag);
        Assert.Equal("--system", flag.Matches[0].Name);
        Assert.True(flag.IsExact("/export markdown "));
    }

    [Fact]
    public void Create_CompletesExportFlagAfterPath()
    {
        var picker = SlashPicker.Create("/export json ./out.json ", ExportOptions());

        Assert.NotNull(picker);
        Assert.Equal("--system", picker.Matches[0].Name);
        Assert.Equal("/export json ./out.json --system ", picker.CompletedText);
    }

    [Fact]
    public void Create_CompletesPromptExportDirectoryWithoutBlockingOptionalSubmit()
    {
        var picker = SlashPicker.Create("/prompts export ", PromptOptions());

        Assert.NotNull(picker);
        Assert.Equal(".", picker.Matches[0].Name);
        Assert.True(picker.IsExact("/prompts export "));
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

    private static SlashOption[] ExportOptions()
    {
        SlashOption[] flag = [new("--system", "include system", ["--system"])];
        var export = new SlashOption(
            "export",
            "export conversation",
            ["export"],
            [
                new("markdown", "Markdown", ["markdown", "md"], flag, true, flag),
                new("json", "JSON", ["json"], flag, true, flag)
            ]);
        return [export, .. Options];
    }

    private static SlashOption[] PromptOptions()
    {
        var prompts = new SlashOption(
            "promptset",
            "prompt sets",
            ["promptset", "prompts"],
            [
                new(
                    "export",
                    "export prompts",
                    ["export"],
                    [new(".", "current workspace", ["."])],
                    ArgumentsOptional: true)
            ]);
        return [prompts, .. Options];
    }

    [Fact]
    public void Move_CyclesSelection()
    {
        var picker = SlashPicker.Create("/", Options);

        Assert.NotNull(picker);
        picker = picker.Move(1);
        Assert.Equal("resume", picker.Matches[picker.Selected].Name);
    }

    [Fact]
    public void Refresh_SameTextPreservesSelection()
    {
        var picker = SlashPicker.Create("/", Options);

        Assert.NotNull(picker);
        picker = picker.Move(1);
        var refreshed = Assert.IsType<SlashPicker>(SlashPicker.Refresh("/", Options, picker));

        Assert.Same(picker, refreshed);
        Assert.Equal("resume", refreshed.Matches[refreshed.Selected].Name);
    }

    [Fact]
    public void Refresh_ChangedTextCreatesFilteredSelection()
    {
        var picker = SlashPicker.Create("/", Options);

        Assert.NotNull(picker);
        picker = picker.Move(1);
        var refreshed = Assert.IsType<SlashPicker>(SlashPicker.Refresh("/q", Options, picker));

        Assert.NotSame(picker, refreshed);
        Assert.Equal("quit", refreshed.Matches[refreshed.Selected].Name);
    }

    [Theory]
    [InlineData("/cl", false)]
    [InlineData("/clear", true)]
    [InlineData("/CLEAR", true)]
    public void IsExact_ComparesCompletedCommand(string text, bool expected)
    {
        var picker = SlashPicker.Create(text, Options);

        Assert.NotNull(picker);
        Assert.Equal(expected, picker.IsExact(text));
    }

    [Fact]
    public void Paint_MovingBelowViewportScrollsToSelectedOption()
    {
        var picker = SlashPicker.Create("/", ManyOptions());

        Assert.NotNull(picker);
        for (var i = 0; i < SlashPicker.MaximumVisible; i++)
        {
            picker = picker.Move(1);
        }

        var lines = picker.Paint(80);

        Assert.Equal(SlashPicker.MaximumVisible, lines.Count);
        Assert.DoesNotContain(lines, line => line.Plain.Contains("/item0", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Plain.Contains("> /item8", StringComparison.Ordinal));
    }

    [Fact]
    public void Paint_MovingUpFromFirstWrapsViewportToLastOption()
    {
        var picker = SlashPicker.Create("/", ManyOptions());

        Assert.NotNull(picker);
        picker = picker.Move(-1);
        var lines = picker.Paint(80);

        Assert.Equal(SlashPicker.MaximumVisible, lines.Count);
        Assert.DoesNotContain(lines, line => line.Plain.Contains("/item0", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Plain.Contains("> /item9", StringComparison.Ordinal));
    }

    private static SlashOption[] ManyOptions() =>
        Enumerable.Range(0, 10)
            .Select(index => new SlashOption($"item{index}", $"item {index}", [$"item{index}"]))
            .ToArray();
}
