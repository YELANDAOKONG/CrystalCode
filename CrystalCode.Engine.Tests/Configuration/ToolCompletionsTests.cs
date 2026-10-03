using CrystalCode.Engine.Configuration;

using Xunit;

namespace CrystalCode.Engine.Tests.Configuration;

public sealed class ToolCompletionsTests
{
    [Fact]
    public void All_OffersSourcePolicyArguments()
    {
        var home = Assert.Single(ToolCompletions.All, option => option.Name == "home");

        Assert.Equal(["author", "host"], home.ArgumentOptions.Select(option => option.Name));
    }
}
