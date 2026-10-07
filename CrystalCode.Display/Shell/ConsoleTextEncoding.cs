using System.Text;

using Spectre.Console;

namespace CrystalCode.Display.Shell;

/// <summary>
/// Console output is UTF-8 without a BOM. Windows OEM code pages replace the
/// progress spinner's braille frames with a question mark. Input encoding stays
/// as the console left it, so key and wheel decoding are unchanged.
/// </summary>
public static class ConsoleTextEncoding
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static EncodingLease? UseUtf8Output()
    {
        ApplySpectre(Utf8);
        try
        {
            var previous = Console.OutputEncoding;
            if (previous.CodePage == Utf8.CodePage)
            {
                return null;
            }

            Console.OutputEncoding = Utf8;
            return new EncodingLease(previous);
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
            AnsiConsole.Profile.Encoding = encoding;
            AnsiConsole.Profile.Capabilities.Unicode = true;
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
