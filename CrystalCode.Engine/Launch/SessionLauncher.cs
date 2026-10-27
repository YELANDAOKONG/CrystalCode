using System.Text.Json;

using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Modes;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Sessions.Interfaces;
using CrystalCode.Engine.Tools;

namespace CrystalCode.Engine.Launch;

/// <summary>
/// Turns a launch request into a validated <see cref="SessionLaunch"/>: it
/// applies launch overrides in memory, checks credentials, resolves the
/// workspace and the saved session to resume, asks for trust, and validates
/// prompt attachments, directory rules, and the launch mode. Every front end
/// that opens a session uses this path. Questions reach the operator only
/// through the supplied prompts; a null prompt means the front end cannot ask.
/// </summary>
public static class SessionLauncher
{
    public const string SpaceWithWorkspace = "--space cannot be combined with --workspace.";

    public const string UntrustedWorkspace =
        "This directory is not trusted. Open it in the terminal and trust it, or pass --workspace-trust off for this process.";

    public const string TrustNeedsOperator = "Trusting this directory requires an operator.";

    public const string ResumeNeedsOperator = "Choosing a session requires an operator. Pass --resume <id>.";

    public static async Task<SessionLaunchResult> PrepareAsync(
        SessionLaunchRequest request,
        IWorkspaceTrustPrompt? trust,
        ISessionChooser? chooser,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkingDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Space && !string.IsNullOrWhiteSpace(request.Workspace))
        {
            return Invalid(SpaceWithWorkspace);
        }

        try
        {
            return await PrepareCoreAsync(request, trust, chooser, cancellationToken);
        }
        catch (Exception exception) when (IsSetupFailure(exception))
        {
            return Invalid(exception.Message);
        }
    }

    private static async Task<SessionLaunchResult> PrepareCoreAsync(
        SessionLaunchRequest request,
        IWorkspaceTrustPrompt? trust,
        ISessionChooser? chooser,
        CancellationToken cancellationToken)
    {
        var options = request.Options;
        var home = CrystalHome.Resolve(options.Home);
        string workspace;
        if (request.Space)
        {
            workspace = OperatorSpace.EnsureCreated(home);
        }
        else
        {
            workspace = Path.GetFullPath(
                string.IsNullOrWhiteSpace(request.Workspace) ? request.WorkingDirectory : request.Workspace);
            if (!Directory.Exists(workspace))
            {
                return Invalid("Workspace directory not found: " + workspace);
            }
        }

        var explicitWorkspace = request.Space || !string.IsNullOrWhiteSpace(request.Workspace);
        var settingsStore = new DiskSettingsStore(home);
        var loaded = settingsStore.LoadOrCreate();
        if (!SessionLaunchOverrides.TryApply(loaded, options, out var applied, out var mode, out var applyError))
        {
            return Invalid(applyError);
        }

        if (!SessionLaunchOverrides.TryAcceptPromptSet(home, options.PromptSet, out var promptError))
        {
            return Invalid(promptError);
        }

        if (options.Theme is not null && !ThemeCatalog.Exists(home, applied.Theme))
        {
            return Invalid("Theme not found  " + applied.Theme);
        }

        var credentials = new CredentialStore(home);
        if (!credentials.TryResolve(applied.ActiveProvider, out _, out var credentialError))
        {
            return Invalid(credentialError);
        }

        SessionDocument? resumed = null;
        if (request.Resume)
        {
            var sessions = new SessionStore(home);
            if (!ResumeRequest.TryParse(
                    request.ResumeTarget,
                    request.WorkingDirectory,
                    sessions,
                    out var target,
                    out var resumeError))
            {
                return Invalid(resumeError);
            }

            switch (target.Target)
            {
                case ResumeRequest.Kind.Session:
                    if (!SessionResume.TryLoad(sessions, workspace, target.Value, out resumed, out resumeError))
                    {
                        return Invalid(resumeError);
                    }

                    break;
                case ResumeRequest.Kind.CurrentWorkspace:
                {
                    var picked = await ChooseAsync(
                        chooser,
                        sessions.List(workspace),
                        listWorkspace: false,
                        "No session for this workspace",
                        cancellationToken);
                    if (picked.Failure is not null)
                    {
                        return picked.Failure;
                    }

                    if (!SessionResume.TryLoad(sessions, workspace, picked.Id, out resumed, out resumeError))
                    {
                        return Invalid(resumeError);
                    }

                    break;
                }
                case ResumeRequest.Kind.Workspace:
                {
                    if (explicitWorkspace && !ResumeRequest.SameDirectory(workspace, target.Value!))
                    {
                        return Invalid("Workspace and --resume path differ.");
                    }

                    workspace = target.Value!;
                    var namedTrust = await CheckTrustAsync(request.Trust, home, applied.WorkspaceTrust, workspace, trust, cancellationToken);
                    if (namedTrust is not null)
                    {
                        return namedTrust;
                    }

                    var picked = await ChooseAsync(
                        chooser,
                        sessions.List(workspace),
                        listWorkspace: false,
                        "No session for this workspace",
                        cancellationToken);
                    if (picked.Failure is not null)
                    {
                        return picked.Failure;
                    }

                    if (!SessionResume.TryLoad(sessions, workspace, picked.Id, out resumed, out resumeError))
                    {
                        return Invalid(resumeError);
                    }

                    break;
                }
                case ResumeRequest.Kind.AllWorkspaces:
                {
                    var picked = await ChooseAsync(
                        chooser,
                        sessions.List(workspaceRoot: null),
                        listWorkspace: true,
                        "No sessions",
                        cancellationToken);
                    if (picked.Failure is not null)
                    {
                        return picked.Failure;
                    }

                    if (!SessionResume.TryLoad(sessions, workspace, picked.Id, out resumed, out resumeError))
                    {
                        return Invalid(resumeError);
                    }

                    if (!ResumeRequest.TryCanonicalWorkspace(resumed.Workspace, out workspace, out resumeError))
                    {
                        return Invalid(resumeError);
                    }

                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(request),
                        target.Target,
                        "Resume target is not supported.");
            }
        }

        var trusted = await CheckTrustAsync(request.Trust, home, applied.WorkspaceTrust, workspace, trust, cancellationToken);
        if (trusted is not null)
        {
            return trusted;
        }

        if (!SessionLaunchOverrides.TryAcceptPromptAttachments(
                home,
                workspace,
                options.PromptAttachment,
                out var attachmentError))
        {
            return Invalid(attachmentError);
        }

        if (!SessionLaunchAccess.TryRead(options, workspace, out var access, out var accessError))
        {
            return Invalid(accessError);
        }

        IReadOnlyList<DirectoryAccess>? roots = null;
        if (request.AdditionalRoots is { } requested
            && !SessionLaunchAccess.TryReadRoots(requested, workspace, out roots, out var rootsError))
        {
            return Invalid(rootsError);
        }

        if (mode is not null && !ModeSelection.TryResolve(home, workspace, mode, out var modeError))
        {
            return Invalid(modeError);
        }

        // The disk baseline for later preference saves. Only the launch
        // provider and model may follow an interactive preference command.
        var persisted = request.PersistSelection
            ? loaded.WithOverrides(options.Provider, options.Model)
            : loaded;
        if (request.Fork && resumed is not null)
        {
            // A branch keeps the conversation, plans, and children, not the
            // saved directory rules; the new session seeds them as a new one does.
            resumed = SessionFork.Create(resumed, SessionStore.NewId(), workspace, DateTimeOffset.UtcNow);
        }

        return SessionLaunchResult.Ready(new SessionLaunch(
            home,
            settingsStore,
            credentials,
            applied,
            persisted,
            workspace,
            resumed,
            mode,
            access,
            SessionLaunchOverrides.UnavailableThinkingNotes(applied),
            roots,
            request.Fork && resumed is not null));
    }

    private static async Task<SessionLaunchResult?> CheckTrustAsync(
        SessionTrustCheck check,
        CrystalHome home,
        bool workspaceTrust,
        string workspace,
        IWorkspaceTrustPrompt? trust,
        CancellationToken cancellationToken)
    {
        var store = new WorkspaceTrustStore(home);
        switch (check)
        {
            case SessionTrustCheck.Skip:
                return null;
            case SessionTrustCheck.Require:
                return store.Contains(workspace)
                    ? null
                    : SessionLaunchResult.Failed(SessionLaunchFailureKind.Untrusted, UntrustedWorkspace);
            case SessionTrustCheck.Prompt:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(check), check, "Trust check is not supported.");
        }

        if (!workspaceTrust || !Directory.Exists(workspace))
        {
            return null;
        }

        var root = new Workspace(workspace).Root;
        if (store.Contains(root))
        {
            return null;
        }

        if (trust is null)
        {
            return SessionLaunchResult.Failed(SessionLaunchFailureKind.TrustNeedsOperator, TrustNeedsOperator);
        }

        if (!await trust.ConfirmAsync(new WorkspaceTrustRequest(root, store.TrustRoot(root)), cancellationToken))
        {
            return SessionLaunchResult.Failed(SessionLaunchFailureKind.Declined, string.Empty);
        }

        store.Remember(root);
        return null;
    }

    private static async Task<(SessionLaunchResult? Failure, string? Id)> ChooseAsync(
        ISessionChooser? chooser,
        IReadOnlyList<SessionSummary> available,
        bool listWorkspace,
        string emptyMessage,
        CancellationToken cancellationToken)
    {
        if (available.Count == 0)
        {
            return (Invalid(emptyMessage), null);
        }

        if (chooser is null)
        {
            return (SessionLaunchResult.Failed(SessionLaunchFailureKind.ResumeNeedsOperator, ResumeNeedsOperator), null);
        }

        var id = await chooser.ChooseAsync(available, currentId: null, listWorkspace, cancellationToken);
        return id is null
            ? (SessionLaunchResult.Failed(SessionLaunchFailureKind.Declined, string.Empty), null)
            : (null, id);
    }

    private static SessionLaunchResult Invalid(string error) =>
        SessionLaunchResult.Failed(SessionLaunchFailureKind.Invalid, error);

    private static bool IsSetupFailure(Exception exception) =>
        exception is (ArgumentException and not ArgumentNullException)
            or InvalidOperationException
            or IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or JsonException;
}
