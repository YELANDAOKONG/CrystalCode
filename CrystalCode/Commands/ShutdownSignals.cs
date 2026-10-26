using System.Runtime.InteropServices;

using CrystalCode.Server.Hosting;

namespace CrystalCode.Commands;

/// <summary>
/// Turns SIGINT (Ctrl+C), SIGTERM, and SIGQUIT into an orderly shutdown of
/// the server instead of an immediate process exit. A second signal while
/// the server is already stopping ends the process at once.
/// </summary>
internal sealed class ShutdownSignals : IDisposable
{
    private readonly List<PosixSignalRegistration> _registrations = [];
    private int _received;

    private ShutdownSignals()
    {
    }

    public static ShutdownSignals Register(Action<string> stop)
    {
        ArgumentNullException.ThrowIfNull(stop);
        var signals = new ShutdownSignals();
        signals.Add(PosixSignal.SIGINT, ServerStopReason.Interrupt, stop);
        signals.Add(PosixSignal.SIGTERM, ServerStopReason.Terminate, stop);
        signals.Add(PosixSignal.SIGQUIT, ServerStopReason.QuitSignal, stop);
        return signals;
    }

    private void Add(PosixSignal signal, string reason, Action<string> stop)
    {
        _registrations.Add(PosixSignalRegistration.Create(signal, context =>
        {
            // The first signal stops cleanly; a repeat lets the runtime exit.
            if (Interlocked.Increment(ref _received) > 1)
            {
                return;
            }

            context.Cancel = true;
            stop(reason);
        }));
    }

    public void Dispose()
    {
        foreach (var registration in _registrations)
        {
            registration.Dispose();
        }
    }
}
