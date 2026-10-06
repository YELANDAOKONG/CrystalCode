using CrystalCode.Commands;

using Xunit;

namespace CrystalCode.Tests.Commands;

public sealed class ExtensionConsoleTests
{
    [Fact]
    public void TryParseFormat_DefaultsToSpectreAndAcceptsText()
    {
        Assert.True(ExtensionConsole.TryParseFormat(null, out var plain, out var error));
        Assert.False(plain);
        Assert.Equal(string.Empty, error);
        Assert.True(ExtensionConsole.TryParseFormat("text", out plain, out error));
        Assert.True(plain);
        Assert.False(ExtensionConsole.TryParseFormat("json", out _, out error));
        Assert.Equal("Format must be text.", error);
    }

    [Fact]
    public void TryParseSource_AcceptsHomeAndProject()
    {
        Assert.True(ExtensionConsole.TryParseSource(" Home ", out var name, out var error));
        Assert.Equal("home", name);
        Assert.Equal(string.Empty, error);
        Assert.False(ExtensionConsole.TryParseSource("both", out _, out error));
        Assert.Equal("Source must be home or project.", error);
    }
}
