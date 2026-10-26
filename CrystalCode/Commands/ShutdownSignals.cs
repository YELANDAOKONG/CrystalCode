using System.Runtime.InteropServices;

using CrystalCode.Server.Hosting;

namespace CrystalCode.Commands;

/// <summary>
/// Turns termination signals into an orderly shutdown instead of an
/// immediate process exit. The server registers SIGINT (Ctrl+C), SIGTERM,
/// and SIGQUIT. The terminal leaves SIGINT to its own Ctrl+C handling and
/// registers SIGTERM and SIGHUP. A second signal while shutdown is already
/// running ends the process at once.
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
        signals.Add(PosixSignal.SIGINT, () => stop(ServerStopReason.Interrupt));
        signals.Add(PosixSignal.SIGTERM, () => stop(ServerStopReason.Terminate));
        signals.Add(PosixSignal.SIGQUIT, () => stop(ServerStopReason.QuitSignal));
        return signals;
    }

    /// <summary>
    /// Runs <paramref name="stop"/> for the first of
    /// <paramref name="signals"/> in place of the default termination. The
    /// runtime calls it on its own thread, so it may block; on Windows the
    /// process ends as soon as a close or shutdown handler returns.
    /// </summary>
    public static ShutdownSignals Register(IReadOnlyList<PosixSignal> signals, Action stop)
    {
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(stop);
        var registrations = new ShutdownSignals();
        foreach (var signal in signals)
        {
            registrations.Add(signal, stop);
        }

        return registrations;
    }

    private void Add(PosixSignal signal, Action stop)
    {
        _registrations.Add(PosixSignalRegistration.Create(signal, context =>
        {
            // The first signal stops cleanly; a repeat lets the runtime exit.
            if (Interlocked.Increment(ref _received) > 1)
            {
                return;
            }

            context.Cancel = true;
            stop();
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
