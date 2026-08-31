using CrystalHarness.Display.Paint;

using Xunit;

namespace CrystalHarness.Tests.Display.Paint;

public sealed class PathDisplayTests
{
    [Fact]
    public void Shorten_ReplacesHomePrefix()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var path = Path.Combine(home, "src", "app");

        var shortened = PathDisplay.Shorten(path);

        Assert.StartsWith("~/", shortened, StringComparison.Ordinal);
        Assert.EndsWith("src/app", shortened, StringComparison.Ordinal);
    }
}
