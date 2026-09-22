using CrystalCode.Sessions;

using Xunit;

namespace CrystalCode.Tests.Sessions;

public sealed class ImageAttachmentTests
{
    private static readonly byte[] PngSignature =
        [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

    [Fact]
    public void Constructor_UnsupportedInlineData_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new ImageAttachment(1, "image/png", new byte[] { 1, 2, 3 }));

        Assert.Contains("not a supported", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_MismatchedInlineMimeType_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new ImageAttachment(1, "image/jpeg", PngSignature));

        Assert.Contains("does not match", exception.Message, StringComparison.Ordinal);
    }
}
