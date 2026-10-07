using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class SlashMenuTests
{
    [Fact]
    public void Create_NestsApprovalModelAndThinkingArguments()
    {
        SlashCompletion[] models =
        [
            new("gpt-5.6-sol", "openai", ["gpt-5.6-sol"]),
            new("openai", "Provider", ["openai"])
        ];
        SlashCompletion[] efforts =
        [
            new("default", "Provider default", ["default"]),
            new("high", "High", ["high"])
        ];

        var menu = SlashMenu.Create(
            extras: null,
            approvalModelArguments: models,
            approvalThinkingArguments: efforts);
        var approval = menu.Single(item => item.Name == "approval");

        var model = approval.ArgumentOptions.Single(item => item.Name == "model");
        Assert.Equal(["gpt-5.6-sol", "openai"], model.ArgumentOptions.Select(item => item.Name));

        var thinking = approval.ArgumentOptions.Single(item => item.Name == "thinking");
        Assert.Equal(["default", "high"], thinking.ArgumentOptions.Select(item => item.Name));

        var review = approval.ArgumentOptions.Single(item => item.Name == "review");
        Assert.Empty(review.ArgumentOptions);
    }

    [Fact]
    public void Create_KeepsApprovalArgumentsFlatWithoutDynamicLists()
    {
        var menu = SlashMenu.Create(extras: null);
        var approval = menu.Single(item => item.Name == "approval");

        Assert.Contains(approval.ArgumentOptions, item => item.Name == "model");
        Assert.Contains(approval.ArgumentOptions, item => item.Name == "thinking");
        Assert.All(approval.ArgumentOptions, item => Assert.Empty(item.ArgumentOptions));
    }
}
