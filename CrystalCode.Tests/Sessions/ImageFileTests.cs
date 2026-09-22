using CrystalCode.Sessions;

using Xunit;

namespace CrystalCode.Tests.Sessions;

public sealed class ImageFileTests
{
    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }, "image/png")]
    [InlineData(new byte[] { 0xff, 0xd8, 0xff }, "image/jpeg")]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, "image/gif")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 }, "image/webp")]
    public void DetectMimeType_UsesFileSignature(byte[] data, string expected)
    {
        Assert.Equal(expected, ImageFile.DetectMimeType(data));
    }

    [Fact]
    public void DetectMimeType_RejectsUnknownData()
    {
        Assert.Null(ImageFile.DetectMimeType([1, 2, 3, 4]));
    }
}
