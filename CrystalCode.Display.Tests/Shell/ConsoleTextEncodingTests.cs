using System.Text;

using CrystalCode.Display.Shell;

using Spectre.Console;

using Xunit;

namespace CrystalCode.Display.Tests.Shell;

public sealed class ConsoleTextEncodingTests
{
    [Fact]
    public void UseUtf8Output_KeepsTheSpinnerFrame()
    {
        var frame = ProgressSpinner.Frame(0);
        using var lease = ConsoleTextEncoding.UseUtf8Output();

        var encoding = Console.OutputEncoding;
        Assert.Equal(frame, encoding.GetString(encoding.GetBytes(frame)));
        Assert.Equal("?", Encoding.ASCII.GetString(Encoding.ASCII.GetBytes(frame)));
    }

    [Fact]
    public void UseUtf8Output_BindsSpectreToTheLiveUtf8Writer()
    {
        using var lease = ConsoleTextEncoding.UseUtf8Output();

        // Spectre encodes text through its profile writer, not through
        // Console.OutputEncoding. Setting the code page replaces Console.Out, so
        // the profile must point at the replacement, or a Windows OEM code page
        // garbles the braille spinner and CJK text.
        var writer = AnsiConsole.Profile.Out.Writer;
        Assert.Same(Console.Out, writer);
        Assert.Equal(Encoding.UTF8.CodePage, writer.Encoding.CodePage);
        Assert.Equal(ProgressSpinner.Frame(0), writer.Encoding.GetString(writer.Encoding.GetBytes(ProgressSpinner.Frame(0))));
        Assert.Equal("你好世界", writer.Encoding.GetString(writer.Encoding.GetBytes("你好世界")));
    }
}
