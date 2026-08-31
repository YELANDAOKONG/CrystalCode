using System.Text;

namespace CrystalHarness.Display.Composer;

/// <summary>
/// Accumulates bracketed paste (CSI 200~ / 201~) across key bursts.
/// </summary>
public sealed class BracketedPaste
{
    public const string StartMarker = "\u001b[200~";

    public const string EndMarker = "\u001b[201~";

    private readonly StringBuilder _held = new();
    private bool _open;

    public bool IsOpen => _open;

    public void Reset()
    {
        _held.Clear();
        _open = false;
    }

    public IReadOnlyList<string> Push(string incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        if (incoming.Length == 0 && !_open)
        {
            return [];
        }

        var buffer = _open ? _held + incoming : incoming;
        if (_open)
        {
            _held.Clear();
        }

        var completed = new List<string>();
        while (true)
        {
            if (!_open)
            {
                var start = buffer.IndexOf(StartMarker, StringComparison.Ordinal);
                if (start < 0)
                {
                    return completed;
                }

                _open = true;
                buffer = buffer[(start + StartMarker.Length)..];
                continue;
            }

            var end = buffer.IndexOf(EndMarker, StringComparison.Ordinal);
            if (end < 0)
            {
                _held.Append(buffer);
                return completed;
            }

            completed.Add(Normalize(buffer[..end]));
            _open = false;
            buffer = buffer[(end + EndMarker.Length)..];
        }
    }

    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }
}
