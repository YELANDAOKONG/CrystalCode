using System.Text;

using CrystalCode.Display.Shell;

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
}
