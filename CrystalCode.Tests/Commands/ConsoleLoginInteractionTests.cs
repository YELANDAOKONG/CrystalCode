using CrystalCode.Terminal;

using Xunit;

namespace CrystalCode.Tests.Commands;

public sealed class ConsoleLoginInteractionTests
{
    private static readonly Uri Link = new("https://example.test/authorize?code_challenge=abc&state=xyz");

    [Fact]
    public async Task OpenLink_WithTheBrowserOffReportsFailureSoTheLinkIsShown()
    {
        var interaction = new ConsoleLoginInteraction(new StringWriter(), new StringReader(string.Empty), openBrowser: false);

        Assert.False(await interaction.OpenLinkAsync(new Uri("https://example.test/"), CancellationToken.None));
    }

    [Fact]
    public async Task Plain_WritesBareLinesWithTheLinkOnItsOwnLine()
    {
        var output = new StringWriter();
        var interaction = new ConsoleLoginInteraction(output, new StringReader("pasted\n"), plain: true);

        await interaction.ShowAsync("Continue in your browser.", CancellationToken.None);
        await interaction.ShowLinkAsync("Open this address:", Link, CancellationToken.None);
        var pasted = await interaction.ReadAsync("Paste it:", CancellationToken.None);

        Assert.Equal("pasted", pasted);
        Assert.Equal(
            new[] { "Continue in your browser.", "Open this address:", Link.AbsoluteUri, "Paste it:" },
            output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task Formatted_KeepsTheLinkWholeAndEscapesMarkupInText()
    {
        var output = new StringWriter();
        var interaction = new ConsoleLoginInteraction(output, new StringReader("pasted\n"), plain: false);

        await interaction.ShowAsync("Use [brackets] safely.", CancellationToken.None);
        await interaction.ShowLinkAsync("Open this address:", Link, CancellationToken.None);
        await interaction.ReadAsync("Paste it:", CancellationToken.None);

        // Spectre colors its lines whenever the writer looks like a terminal.
        var text = System.Text.RegularExpressions.Regex.Replace(output.ToString(), "\u001b\\[[0-9;]*m", string.Empty);
        var lines = text.Split('\n').Select(line => line.TrimEnd()).ToArray();
        Assert.Contains("Use [brackets] safely.", lines);
        Assert.Contains(Link.AbsoluteUri, lines);
        Assert.Contains("Paste it:", lines);
    }

    [Theory]
    [InlineData("http://127.0.0.1:1455/auth/callback?code=x", true)]
    [InlineData("https://example.test/", true)]
    [InlineData("code=x", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("", false)]
    public void Popup_AcceptsOnlyHttpAddresses(string text, bool accepted) =>
        Assert.Equal(accepted, LoginPopup.IsAddress(text));
}
