namespace CrystalCode.Terminal;

/// <summary>
/// Stops Ctrl+C from killing the process while a startup screen is open.
/// The runtime would otherwise exit before the alternate screen can show the cursor.
/// </summary>
internal sealed class TerminalInterrupt : IDisposable
{
    private readonly CancellationTokenSource _cancel;
    private bool _disposed;

    private TerminalInterrupt(CancellationTokenSource cancel)
    {
        _cancel = cancel;
        Console.CancelKeyPress += OnCancel;
    }

    public CancellationToken Token => _cancel.Token;

    public static TerminalInterrupt Link(CancellationToken cancellationToken)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        return new TerminalInterrupt(linked);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Console.CancelKeyPress -= OnCancel;
        _cancel.Dispose();
    }

    private void OnCancel(object? sender, ConsoleCancelEventArgs args)
    {
        args.Cancel = true;
        CancelReads();
    }

    internal void CancelReads()
    {
        try
        {
            _cancel.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
