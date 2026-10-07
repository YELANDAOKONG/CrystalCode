using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class PromptAttachmentListTests
{
    [Fact]
    public void TryEnable_AppendsANewName()
    {
        var enabled = PromptAttachmentList.TryEnable(
            ["alpha"],
            "beta",
            available: true,
            out var next,
            out var error);

        Assert.True(enabled);
        Assert.Equal(["alpha", "beta"], next);
        Assert.Equal(string.Empty, error);
    }

    [Fact]
    public void TryEnable_RejectsANameThatIsMissingOrAlreadyEnabled()
    {
        Assert.False(PromptAttachmentList.TryEnable(
            [],
            "missing",
            available: false,
            out _,
            out var missing));
        Assert.Contains("not found", missing, StringComparison.Ordinal);

        Assert.False(PromptAttachmentList.TryEnable(
            ["alpha"],
            "alpha",
            available: true,
            out _,
            out var duplicate));
        Assert.Contains("already enabled", duplicate, StringComparison.Ordinal);
    }

    [Fact]
    public void TryMove_SwapsWithinTheEnabledList()
    {
        Assert.True(PromptAttachmentList.TryMove(
            ["alpha", "beta"],
            "beta",
            earlier: true,
            out var next,
            out _));
        Assert.Equal(["beta", "alpha"], next);

        Assert.False(PromptAttachmentList.TryMove(
            ["alpha"],
            "alpha",
            earlier: true,
            out _,
            out var first));
        Assert.Contains("already first", first, StringComparison.Ordinal);

        Assert.False(PromptAttachmentList.TryMove(
            ["alpha"],
            "alpha",
            earlier: false,
            out _,
            out var last));
        Assert.Contains("already last", last, StringComparison.Ordinal);
    }

    [Fact]
    public void TryDisable_RemovesEveryCopy()
    {
        var disabled = PromptAttachmentList.TryDisable(
            ["alpha", "beta", "alpha"],
            "alpha",
            out var next,
            out var error);

        Assert.True(disabled);
        Assert.Equal(["beta"], next);
        Assert.Equal(string.Empty, error);
    }
}
