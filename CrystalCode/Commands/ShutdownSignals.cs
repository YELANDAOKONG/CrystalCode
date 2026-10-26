using System.Runtime.InteropServices;

namespace CrystalCode.Commands;

/// <summary>
/// Turns SIGINT, SIGTERM, and SIGQUIT into an orderly shutdown of the
/// server instead of an immediate process exit.
/// </summary>
internal sealed class ShutdownSignals : IDisposable
{
    private readonly List<PosixSignalRegistration> _registrations = [];

    private ShutdownSignals()
    {
    }

    public static ShutdownSignals Register(CancellationTokenSource stop)
    {
        ArgumentNullException.ThrowIfNull(stop);
        var signals = new ShutdownSignals();
        foreach (var signal in new[] { PosixSignal.SIGINT, PosixSignal.SIGTERM, PosixSignal.SIGQUIT })
        {
            signals._registrations.Add(PosixSignalRegistration.Create(signal, context =>
            {
                context.Cancel = true;
                try
                {
                    stop.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
            }));
        }

        return signals;
    }

    public void Dispose()
    {
        foreach (var registration in _registrations)
        {
            registration.Dispose();
        }
    }
}
