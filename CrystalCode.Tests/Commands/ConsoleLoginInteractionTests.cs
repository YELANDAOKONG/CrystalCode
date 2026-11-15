using CrystalCode.Terminal;

using Xunit;

namespace CrystalCode.Tests.Commands;

public sealed class ConsoleLoginInteractionTests
{
    [Fact]
    public async Task OpenLink_WithTheBrowserOffReportsFailureSoTheLinkIsShown()
    {
        var interaction = new ConsoleLoginInteraction(new StringWriter(), new StringReader(string.Empty), openBrowser: false);

        Assert.False(await interaction.OpenLinkAsync(new Uri("https://example.test/"), CancellationToken.None));
    }
}
