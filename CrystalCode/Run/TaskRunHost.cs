using System.Text.Json;

using CrystalCode.Commands;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Prompts;
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

        var workspace = ResolveWorkspace(settings.Workspace);
        if (!Directory.Exists(workspace))
        {
            await error.WriteLineAsync("Workspace directory not found: " + workspace);
            return RunExit.Invalid;
        }

        HarnessSettings applied;
        bool planMode;
        CrystalHome home;
        try
        {
            home = CrystalHome.Resolve(settings.Home);
            var loaded = new SettingsStore(home).LoadOrCreate();
            if (!TaskRunOverrides.TryApply(loaded, settings, out applied, out planMode, out var applyError))
            {
                await error.WriteLineAsync(applyError);
                return RunExit.Invalid;
            }
        }
        catch (Exception exception) when (IsSetupFailure(exception))
        {
            await error.WriteLineAsync(exception.Message);
            return RunExit.Invalid;
        }

        if (settings.PromptSet is not null && !new PromptStore(home).ContainsSet(settings.PromptSet))
        {
            await error.WriteLineAsync("Prompt set not found  " + settings.PromptSet.Trim());
            return RunExit.Invalid;
        }

        var credentials = new CredentialStore(home);
        if (!credentials.TryResolve(applied.ActiveProvider, out _, out var credentialError))
        {
            await error.WriteLineAsync(credentialError);
            return RunExit.Invalid;
        }

        var approvals = new UnattendedApprovalPrompt();
        var questions = new UnattendedUserPrompt();
        IRunLog log = format == RunFormat.Json
            ? new RunJsonLog(output, settings.ShowThinking)
            : new RunLog(output, settings.ShowThinking);
        CodingSession session;
        try
        {
            session = CodingSession.Create(
                applied,
                new SettingsStore(home),
                credentials,
                home,
                workspace,
                new SessionFrontEnd(
                    log,
                    approvals,
                    questions,
                    new UnattendedSessionChooser()),
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
                if (planMode && !session.PlanMode)
                {
                    session.TogglePlan();
                }

                await session.SubmitAsync(task, cancellationToken);
                if (session.TurnTask is null)
                {
                    await error.WriteLineAsync("The task did not start a turn.");
                }
                else
                {
                    using (cancellationToken.Register(
                        static state => ((CodingSession)state!).TryInterrupt(),
                        session))
                    {
                        await session.TurnTask;
                    }

                    await session.CompleteTurnAsync(CancellationToken.None);
                    if (log.StopReason is null)
                    {
                        await error.WriteLineAsync("The task did not finish a turn.");
                    }
                    else
                    {
                        var operatorDenied = approvals.Asked > 0 || questions.Dismissed > 0;
                        (code, status) = RunExit.From(log.StopReason, operatorDenied);
                    }
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

                    await session.CompleteTurnAsync(CancellationToken.None);
                }

                code = RunExit.Interrupted;
                status = "interrupted";
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
            var hint = session.Close();
            log.WriteEpilogue(status, hint);
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

    private static string ResolveWorkspace(string? workspace)
    {
        if (string.IsNullOrWhiteSpace(workspace))
        {
            return Path.GetFullPath(Environment.CurrentDirectory);
        }

        return Path.GetFullPath(workspace);
    }

    private static bool IsSetupFailure(Exception exception) =>
        exception is (ArgumentException and not ArgumentNullException)
            or InvalidOperationException
            or IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or JsonException;
}
