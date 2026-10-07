using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class PromptAttachmentChangeArgumentsTests
{
    [Fact]
    public void TryParse_AcceptsOneActionAndOneName()
    {
        var parsed = PromptAttachmentChangeArguments.TryParse(
            ["Up", "alpha"],
            out var action,
            out var name,
            out var error);

        Assert.True(parsed);
        Assert.Equal("up", action);
        Assert.Equal("alpha", name);
        Assert.Equal(string.Empty, error);
    }

    [Fact]
    public void TryParse_RejectsAMissingNameOrUnknownAction()
    {
        Assert.False(PromptAttachmentChangeArguments.TryParse(
            ["enable"],
            out _,
            out _,
            out var missing));
        Assert.False(PromptAttachmentChangeArguments.TryParse(
            ["export", "alpha"],
            out _,
            out _,
            out var unknown));
        Assert.Contains("enable, disable, up, or down", missing, StringComparison.Ordinal);
        Assert.Contains("enable, disable, up, or down", unknown, StringComparison.Ordinal);
    }
}
