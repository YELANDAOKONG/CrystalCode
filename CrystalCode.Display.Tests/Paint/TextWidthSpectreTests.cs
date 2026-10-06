using System.Text;

using CrystalCode.Display.Paint;

using Spectre.Console.Rendering;

using Xunit;

namespace CrystalCode.Display.Tests.Paint;

/// <summary>
/// Rows are written through Spectre, which wraps a row that it measures as wider
/// than the console. Our width must therefore never be smaller than Spectre's.
/// </summary>
public sealed class TextWidthSpectreTests
{
    [Fact]
    public void Measure_IsNeverNarrowerThanSpectreForAnyCodePoint()
    {
        var narrower = new StringBuilder();
        for (var codePoint = 0xA0; codePoint <= 0x10FFFF; codePoint++)
        {
            if (codePoint is >= 0xD800 and <= 0xDFFF)
            {
                continue;
            }

            var text = char.ConvertFromUtf32(codePoint);
            var ours = TextWidth.Measure(text);
            var theirs = new Segment(text).CellCount();
            if (ours < theirs)
            {
                narrower.Append("U+").Append(codePoint.ToString("X", System.Globalization.CultureInfo.InvariantCulture))
                    .Append(' ').Append(ours).Append('<').Append(theirs).Append("; ");
            }
        }

        Assert.True(narrower.Length == 0, narrower.ToString());
    }

    [Theory]
    [InlineData("\u2705")]
    [InlineData("\u274c")]
    [InlineData("\u26a1")]
    [InlineData("\u2728")]
    [InlineData("\u2b50")]
    [InlineData("\U0001F004")]
    [InlineData("\U0001F680")]
    [InlineData("\u4f60")]
    public void Measure_CountsDoubleWidthSymbolsAsTwoCells(string text)
    {
        Assert.Equal(2, TextWidth.Measure(text));
    }

    [Theory]
    [InlineData("\u00b7")]
    [InlineData("\u2022")]
    [InlineData("\u2500")]
    [InlineData("\u26a0")]
    [InlineData("\U0001F5A5")]
    public void Measure_CountsNarrowSymbolsAsOneCell(string text)
    {
        Assert.Equal(1, TextWidth.Measure(text));
    }
}
