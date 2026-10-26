using System.Runtime.InteropServices;

using CrystalCode.Commands;
using CrystalCode.Display.Composer;
using CrystalCode.Display.Shell;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Launch;
using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Sessions;
using CrystalCode.Terminal.Approvals;

namespace CrystalCode.Terminal;

/// <summary>
/// Runs one coding session in the terminal. It owns the alternate screen, the
/// key loop, and Ctrl+C. Every product decision stays in the session.
/// </summary>
internal sealed class TerminalHost
{
    /// <summary>
    /// SIGTERM is a kill or system shutdown; SIGHUP is a closed terminal
    /// window (CTRL_CLOSE_EVENT on Windows). SIGINT stays with Ctrl+C.
    /// </summary>
    private static readonly PosixSignal[] ShutdownSignalSet = [PosixSignal.SIGTERM, PosixSignal.SIGHUP];

    /// <summary>
    /// How long a signal handler holds the process open for the save. Windows
    /// ends the process when the handler returns and allows about five
    /// seconds for a closing console window.
    /// </summary>
    private static readonly TimeSpan SignalGrace = TimeSpan.FromSeconds(4);

    private readonly SessionRenderer _renderer;
    private readonly SessionProjection _projection;
    private readonly CodingSession _session;
    private readonly string? _launchMode;
    private int _idleCancels;

    private TerminalHost(SessionRenderer renderer, CodingSession session, string? launchMode, SessionProjection projection)
    {
        _projection = projection;
        _renderer = renderer;
        _session = session;
        _launchMode = launchMode;
    }

    public static TerminalHost Create(SessionLaunch launch, PluginRegistry plugins)
    {
        ArgumentNullException.ThrowIfNull(launch);
        ArgumentNullException.ThrowIfNull(plugins);
        var renderer = new SessionRenderer();
        var projection = new SessionProjection(renderer);
        var frontEnd = new SessionFrontEnd(
            projection,
            new ApprovalPrompt(renderer),
            new QuestionPrompt(renderer),
            new SessionPicker(renderer),
            new TrustPrompt(renderer))
        {
            ChooseChildAsync = new ChildPicker(renderer).ChooseAsync
        };
        var session = launch.CreateSession(frontEnd, plugins);
        renderer.LoadQuickPromptsAsync = new QuickPromptStore(launch.Home).LoadAsync;
        renderer.ChildSessionsEnabled = session.AgentsEnabled;
        renderer.QueueQuickPromptAsync = (text, token) => session.SubmitQuickPromptAsync(text, true, token);
        return new TerminalHost(renderer, session, launch.Mode, projection);
    }

    /// <summary>
    /// Selects the launch mode only when a flag named one. A missing flag
    /// leaves a resumed mode in place. The name was checked before launch.
    /// </summary>
    internal static void ApplyLaunchMode(CodingSession session, string? launchMode)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (launchMode is not null
            && !session.TrySetMode(launchMode, out var error))
        {
            throw new ArgumentException(error, nameof(launchMode));
        }
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var closed = new ManualResetEventSlim();
        using var signals = ShutdownSignals.Register(ShutdownSignalSet, () =>
        {
            try
            {
                _session.TryInterrupt();
                stop.Cancel();
                closed.Wait(SignalGrace);
            }
            catch (ObjectDisposedException)
            {
                // The session already closed; nothing is left to save.
            }
        });
        try
        {
            return await RunLoopAsync(stop.Token);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            return 0;
        }
        finally
        {
            try
            {
                await CloseSessionAsync();
            }
            finally
            {
                closed.Set();
            }
        }
    }

    /// <summary>
    /// Saves and closes the session even when the loop left by an exception
    /// with a turn still running. The screen is already closed here, so the
    /// turn is collected without the key pump.
    /// </summary>
    private async Task CloseSessionAsync()
    {
        try
        {
            if (_session.TurnTask is not null)
            {
                _session.TryInterrupt();
                await _session.FinishTurnAsync(CancellationToken.None);
            }
        }
        finally
        {
            WriteHint((await _session.CloseAsync()).Hint);
        }
    }

    private static void WriteHint(string hint)
    {
        try
        {
            Console.WriteLine(hint);
        }
        catch (IOException)
        {
            // The terminal is gone after SIGHUP. The session is already saved.
        }
    }

    private async Task<int> RunLoopAsync(CancellationToken cancellationToken)
    {
        using var screen = _renderer.Open();
        _renderer.OnImagePasteAsync = _session.PasteClipboardImageAsync;
        _renderer.OnComposerEdited = _session.NotifyDraftChanged;
        _renderer.OnSideCleared = _session.ClearSideQuestions;
        _renderer.OnSideCancelled = () => _session.TryCancelSideQuestion();
        _renderer.OnVerboseToggled = PersistVerboseToggle;
        await _session.StartAsync(cancellationToken);
        _renderer.ChildSessionsEnabled = _session.AgentsEnabled;
        ApplyLaunchMode(_session, _launchMode);

        using var promptSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);

        Console.CancelKeyPress += (_, args) =>
        {
            args.Cancel = true;
            if (_renderer.SideQuestionOpen)
            {
                _session.TryCancelSideQuestion();
                _renderer.DismissSideQuestion();
                return;
            }

            if (_projection.ReturnToParent())
            {
                _idleCancels = 0;
                return;
            }

            if (_renderer.CanRecallIntoComposer && _session.TryRecall())
            {
                _idleCancels = 0;
                return;
            }

            if (_session.RecallPending)
            {
                _idleCancels = 0;
                return;
            }

            if (_session.TryInterrupt())
            {
                return;
            }

            if (_renderer.TryClearComposer())
            {
                _idleCancels = 0;
                return;
            }

            _idleCancels++;
            promptSource.Cancel();
        };

        while (true)
        {
            PromptRead read;
            try
            {
                read = await _renderer.ReadPromptAsync(
                    _session.PlanMode,
                    _session.CycleMode,
                    _session.TurnTask,
                    preserveStream: _session.TurnActive,
                    ignorePause: false,
                    promptSource.Token);
                _idleCancels = 0;
            }
            catch (OperationCanceledException)
            {
                if (_idleCancels >= 2 || cancellationToken.IsCancellationRequested)
                {
                    await CollectTurnAsync(promptSource.Token);
                    return 0;
                }

                if (!promptSource.IsCancellationRequested)
                {
                    continue;
                }

                _renderer.WriteNote("Ctrl+C again to exit");
                if (!promptSource.TryReset())
                {
                    await CollectTurnAsync(promptSource.Token);
                    return 0;
                }

                continue;
            }

            if (read.TurnEnded)
            {
                var quitAfterTurn = await PumpAsync(
                    _session.CompleteTurnAsync(promptSource.Token),
                    promptSource.Token);
                if (quitAfterTurn)
                {
                    return 0;
                }

                continue;
            }

            if (read.IsQuickPrompt || !string.IsNullOrWhiteSpace(read.Text) && !read.Text.TrimStart().StartsWith('/'))
            {
                _projection.ReturnToParent();
            }
            if (read.IsQuickPrompt)
            {
                await PumpAsync(_session.SubmitQuickPromptAsync(read.Text, false, promptSource.Token), promptSource.Token);
                continue;
            }

            var quit = await PumpAsync(
                _session.SubmitAsync(read.Text, promptSource.Token),
                promptSource.Token);
            _renderer.ChildSessionsEnabled = _session.AgentsEnabled;
            if (quit)
            {
                return 0;
            }
        }
    }

    private void PersistVerboseToggle(DisplayInput.VerboseToggle toggle)
    {
        switch (toggle)
        {
            case DisplayInput.VerboseToggle.Tools:
                _session.SetVerbose(VerboseTarget.Tools, _renderer.VerboseTools);
                break;
            case DisplayInput.VerboseToggle.Commands:
                _session.SetVerbose(VerboseTarget.Commands, _renderer.VerboseCommands);
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// Interrupts a turn that is still running at exit and collects it while
    /// the frame is open, so its transcript is saved. The collection itself
    /// gets no token: the exit token is already cancelled, and a cancelled
    /// collection would skip the transcript update and let the close save an
    /// older state over the checkpoint.
    /// </summary>
    private async Task CollectTurnAsync(CancellationToken cancellationToken)
    {
        if (_session.TurnTask is null)
        {
            return;
        }

        _session.TryInterrupt();
        await PumpAsync(_session.FinishTurnAsync(CancellationToken.None), cancellationToken);
    }

    /// <summary>
    /// Keeps the frame, spinner, and composer alive while the session works,
    /// and queues anything submitted meanwhile. Blank Enter is forwarded too,
    /// so a waiting follow-up can interrupt the turn or compaction.
    /// </summary>
    private async Task PumpAsync(Task work, CancellationToken cancellationToken)
    {
        if (!work.IsCompleted)
        {
            try
            {
                await _renderer.PumpUntilAsync(
                    work,
                    _session.EnqueueAsync,
                    _session.PlanMode,
                    _session.CycleMode,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (!work.IsCompleted)
            {
                // The cancellation that stopped the pump also reaches the work,
                // which is awaited below so nothing runs unobserved.
            }
        }

        await work;
    }

    private async Task<bool> PumpAsync(Task<bool> work, CancellationToken cancellationToken)
    {
        await PumpAsync((Task)work, cancellationToken);
        return await work;
    }
}
