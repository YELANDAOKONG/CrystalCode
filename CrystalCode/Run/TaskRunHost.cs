using System.Text.Json;

using CrystalCode.Commands;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Launch;
using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Sessions;

namespace CrystalCode.Run;

/// <summary>
/// Loads one process-local session, runs a single user turn, and prints a
/// plain-text log. Flags are not written to <c>config.json</c>.
/// </summary>
internal static class TaskRunHost
{
    public static async Task<int> ExecuteAsync(
        TaskRunSettings settings,
        TextReader input,
        TextWriter output,
        TextWriter error,
        bool inputRedirected,
        PluginRegistry? plugins,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        if (!RunFormat.TryParse(settings.Format, out var format, out var formatError))
        {
            await error.WriteLineAsync(formatError);
            return RunExit.Invalid;
        }

        string task;
        try
        {
            var read = await ReadTaskAsync(settings, input, inputRedirected, cancellationToken);
            if (read is null)
            {
                await error.WriteLineAsync("Pass a task, or pipe the task on stdin.");
                return RunExit.Invalid;
            }

            task = read;
        }
        catch (IOException exception)
        {
            await error.WriteLineAsync(exception.Message);
            return RunExit.Invalid;
        }

        if (SessionCommand.TryParse(task, out _))
        {
            await error.WriteLineAsync("crystal run accepts one task, not a slash command.");
            return RunExit.Invalid;
        }

        if (!SessionLaunchOverrides.TryReadWorkspaceTrust(
                settings.WorkspaceTrust,
                out var checkWorkspaceTrust,
                out var trustError))
        {
            await error.WriteLineAsync(trustError);
            return RunExit.Invalid;
        }

        // A run has no operator: trust is checked, never asked, and the
        // launch provider and model are never eligible for a later save.
        var prepared = await SessionLauncher.PrepareAsync(
            new SessionLaunchRequest(
                settings.ToOptions(),
                Environment.CurrentDirectory,
                settings.Workspace,
                settings.Space,
                Trust: checkWorkspaceTrust ? SessionTrustCheck.Require : SessionTrustCheck.Skip,
                PersistSelection: false),
            trust: null,
            chooser: null,
            cancellationToken);
        if (prepared.Launch is not { } launch)
        {
            await error.WriteLineAsync(prepared.Error);
            return prepared.Failure == SessionLaunchFailureKind.Untrusted
                ? RunExit.Denied
                : RunExit.Invalid;
        }

        foreach (var note in launch.Notes)
        {
            await error.WriteLineAsync(note);
        }

        // A run has no operator to notify. The saved setting stays on disk.
        launch = launch with { Settings = launch.Settings.WithOperatorNotify(OperatorNotifySettings.Off) };
        var approvals = new UnattendedApprovalPrompt();
        var questions = new UnattendedUserPrompt();
        IRunLog log = format == RunFormat.Json
            ? new RunJsonLog(output, settings.ShowThinking)
            : new RunLog(output, settings.ShowThinking);
        CodingSession session;
        try
        {
            session = launch.CreateSession(
                new SessionFrontEnd(
                    log,
                    approvals,
                    questions,
                    new UnattendedSessionChooser(),
                    new UnattendedTrustPrompt()),
                plugins ?? PluginRegistry.CreateBuiltIn());
        }
        catch (Exception exception) when (IsSetupFailure(exception))
        {
            await error.WriteLineAsync(exception.Message);
            return RunExit.Invalid;
        }

        log.BindSession(session.SessionId);
        var code = RunExit.Invalid;
        string? status = null;
        try
        {
            try
            {
                await session.StartAsync(cancellationToken);
                if (launch.Mode is not null && !session.TrySetMode(launch.Mode, out var modeError))
                {
                    await error.WriteLineAsync(modeError);
                    code = RunExit.Invalid;
                    return code;
                }

                var runner = new SessionRunner(session);
                var submitted = runner.SubmitAsync(task);
                runner.Complete();
                var quit = await runner.RunAsync(cancellationToken);
                var accepted = await submitted;
                if (quit)
                {
                    code = RunExit.Interrupted;
                    status = "quit";
                }
                else if (!accepted.TurnActive)
                {
                    await error.WriteLineAsync("The task did not start a turn.");
                }
                else if (log.StopReason is null)
                {
                    // The turn faulted before it produced a result. The
                    // engine has already published the error.
                    code = RunExit.Failed;
                    status = TurnStopReason.Failed.Value;
                }
                else
                {
                    var operatorDenied = approvals.Asked > 0 || questions.Dismissed > 0;
                    (code, status) = RunExit.From(log.StopReason, operatorDenied);
                }
            }
            catch (OperationCanceledException)
            {
                session.TryInterrupt();
                if (session.TurnTask is not null)
                {
                    try
                    {
                        await session.TurnTask;
                    }
                    catch (OperationCanceledException)
                    {
                    }

                    if (await session.CompleteTurnAsync(CancellationToken.None))
                    {
                        status = "quit";
                    }
                }

                code = RunExit.Interrupted;
                status ??= "interrupted";
            }
            catch (Exception exception) when (IsSetupFailure(exception))
            {
                await error.WriteLineAsync(exception.Message);
                code = RunExit.Invalid;
                status = null;
            }
        }
        finally
        {
            var closure = await session.CloseAsync();
            log.WriteEpilogue(status, closure.Hint);
        }

        return code;
    }

    private static async Task<string?> ReadTaskAsync(
        TaskRunSettings settings,
        TextReader input,
        bool inputRedirected,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(settings.TaskText))
        {
            return settings.TaskText.Trim();
        }

        if (!inputRedirected)
        {
            return null;
        }

        var text = await input.ReadToEndAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static bool IsSetupFailure(Exception exception) =>
        exception is (ArgumentException and not ArgumentNullException)
            or InvalidOperationException
            or IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or JsonException;
}
