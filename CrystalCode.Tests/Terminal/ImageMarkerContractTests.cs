using CrystalCode.Display.Composer;
using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Tests.Terminal;

public sealed class ImageMarkerContractTests
{
    [Fact]
    public void Prefix_MatchesTheComposerSubmissionPrefix()
    {
        // The engine owns the encoding; the composer cannot reference it, so
        // the two constants are kept equal by this test.
        Assert.Equal(ImageMarkerText.Prefix, ComposerBuffer.ImageMarkerPrefix);
    }
}
