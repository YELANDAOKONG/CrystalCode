using System.Text;

using Spectre.Console;

namespace CrystalCode.Display.Shell;

/// <summary>
/// Console output is UTF-8 without a BOM. A Windows OEM code page replaces the
/// progress spinner's braille frames with a question mark and garbles CJK text.
/// The code page is switched to UTF-8 and Spectre is rebound to the UTF-8
/// writer so its own encoding matches. Input encoding stays as the console left
/// it, so key and wheel decoding are unchanged.
/// </summary>
public static class ConsoleTextEncoding
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static EncodingLease? UseUtf8Output()
    {
        try
        {
            var previous = Console.OutputEncoding;
            if (previous.CodePage != Utf8.CodePage)
            {
                Console.OutputEncoding = Utf8;
            }

            ApplySpectre(Utf8);
            return previous.CodePage == Utf8.CodePage ? null : new EncodingLease(previous);
        }
        catch (Exception exception) when (exception is IOException
            or ArgumentException
            or PlatformNotSupportedException)
        {
            return null;
        }
    }

    private static void ApplySpectre(Encoding encoding)
    {
        try
        {
            var profile = AnsiConsole.Profile;
            profile.Encoding = encoding;
            // Changing the code page replaces Console.Out, but the default
            // profile captured the previous writer. Rebind so text is encoded
            // with the code page the console now decodes.
            profile.Out = new AnsiConsoleOutput(Console.Out);
            profile.Capabilities.Unicode = true;
        }
        catch (Exception exception) when (exception is IOException
            or ArgumentException
            or PlatformNotSupportedException)
        {
        }
    }

    public sealed class EncodingLease : IDisposable
    {
        private readonly Encoding _previous;
        private int _restored;

        public EncodingLease(Encoding previous)
        {
            ArgumentNullException.ThrowIfNull(previous);
            _previous = previous;
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _restored, 1) != 0)
            {
                return;
            }

            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
            try
            {
                Console.OutputEncoding = _previous;
                ApplySpectre(_previous);
            }
            catch (Exception exception) when (exception is IOException
                or ArgumentException
                or PlatformNotSupportedException)
            {
            }
        }

        private void OnProcessExit(object? sender, EventArgs args) => Dispose();
    }
}
