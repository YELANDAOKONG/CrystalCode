using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class ClipboardImageReaderTests
{
    [Fact]
    public void SupportedTypesInOrder_UsesImagePriority()
    {
        var types = ClipboardImageReader.SupportedTypesInOrder(
            "text/plain\nimage/webp\nimage/jpeg\nimage/gif\n");

        Assert.Equal(["image/jpeg", "image/gif", "image/webp"], types);
    }

    [Fact]
    public void MacClipboardTypes_UsesConvertibleFormats()
    {
        Assert.Equal(
            ["\"PNGf\"", "JPEG picture", "GIF picture"],
            ClipboardImageReader.MacClipboardTypes);
    }

    [Fact]
    public void CreateStartInfo_RedirectsStdinSoTheTerminalStaysRaw()
    {
        var startInfo = ClipboardImageReader.CreateStartInfo("xclip", ["-o"]);

        Assert.True(startInfo.RedirectStandardInput);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
        Assert.Equal(["-o"], startInfo.ArgumentList);
    }

    [Theory]
    [InlineData(0, 0, "Empty")]
    [InlineData(0, 2, "Success")]
    [InlineData(1, 0, "Failed")]
    [InlineData(2, 2, "Failed")]
    public void ClassifyExit_DistinguishesExitFailureFromEmptyOutput(
        int exitCode,
        int outputLength,
        string expected)
    {
        Assert.Equal(expected, ClipboardImageReader.ClassifyExit(exitCode, outputLength).ToString());
    }
}
