using CrystalCode.Display.Composer;
using CrystalCode.Display.Shell;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Home;
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
    private readonly SessionRenderer _renderer;
    private readonly CodingSession _session;
    private int _idleCancels;

    private TerminalHost(SessionRenderer renderer, CodingSession session)
    {
        _renderer = renderer;
        _session = session;
    }

    public static TerminalHost Create(
        HarnessSettings settings,
        SettingsStore settingsStore,
        CredentialStore credentials,
        CrystalHome home,
        string workspaceRoot,
        PluginRegistry plugins,
        SessionDocument? resume)
    {
        var renderer = new SessionRenderer();
        var frontEnd = new SessionFrontEnd(
            new SessionProjection(renderer),
            new ApprovalPrompt(renderer),
            new QuestionPrompt(renderer),
            new SessionPicker(renderer));
        var session = CodingSession.Create(
            settings,
            settingsStore,
            credentials,
            home,
            workspaceRoot,
            frontEnd,
            plugins,
            resume);
        return new TerminalHost(renderer, session);
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await RunLoopAsync(cancellationToken);
        }
        finally
        {
            Console.WriteLine(await _session.CloseAsync());
        }
    }

    private async Task<int> RunLoopAsync(CancellationToken cancellationToken)
    {
        using var screen = _renderer.Open();
        _renderer.OnImagePasteAsync = _session.PasteClipboardImageAsync;
        _renderer.OnComposerEdited = _session.NotifyDraftChanged;
        _renderer.OnSideCleared = _session.ClearSideQuestions;
        _renderer.OnVerboseToggled = PersistVerboseToggle;
        await _session.StartAsync(cancellationToken);

        using var promptSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);

        Console.CancelKeyPress += (_, args) =>
        {
            args.Cancel = true;
            if (_renderer.SideQuestionOpen)
            {
                _session.TryCancelSideQuestion();
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
                    _session.TogglePlan,
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
                    await PumpAsync(
                        _session.FinishTurnAsync(promptSource.Token),
                        promptSource.Token);
                    return 0;
                }

                if (!promptSource.IsCancellationRequested)
                {
                    continue;
                }

                _renderer.WriteNote("Ctrl+C again to exit");
                if (!promptSource.TryReset())
                {
                    await PumpAsync(
                        _session.FinishTurnAsync(promptSource.Token),
                        promptSource.Token);
                    return 0;
                }

                continue;
            }

            if (read.TurnEnded)
            {
                await PumpAsync(
                    _session.CompleteTurnAsync(promptSource.Token),
                    promptSource.Token);
                continue;
            }

            var quit = await PumpAsync(
                _session.SubmitAsync(read.Text, promptSource.Token),
                promptSource.Token);
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
    /// Keeps the frame, spinner, and composer alive while the session works,
    /// and queues anything submitted meanwhile.
    /// </summary>
    private async Task PumpAsync(Task work, CancellationToken cancellationToken)
    {
        if (!work.IsCompleted)
        {
            try
            {
                await _renderer.PumpUntilAsync(
                    work,
                    _session.Enqueue,
                    _session.PlanMode,
                    _session.TogglePlan,
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
