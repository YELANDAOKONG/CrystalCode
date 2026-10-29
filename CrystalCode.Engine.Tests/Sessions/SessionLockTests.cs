using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Tests.Approvals;
using CrystalCode.Engine.Tests.Home;
using CrystalCode.Engine.Tests.Tools;
using CrystalCode.Engine.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class SessionLockTests
{
    private const string HeldId = "heldid";

    [Fact]
    public void Create_RefusesASessionAnotherSessionHolds()
    {
        using var holder = OpenHolder();

        var exception = Assert.Throws<InvalidOperationException>(() => OpenSecond(holder, Saved(holder)));

        Assert.Equal(SessionResume.InUseMessage(HeldId), exception.Message);
    }

    [Fact]
    public async Task Resume_RefusesASessionAnotherSessionHoldsAndKeepsTheCurrentOne()
    {
        using var holder = OpenHolder();
        var second = OpenSecond(holder, null);
        var original = second.Session.SessionId;

        var quit = await second.Session.SubmitAsync("/resume " + HeldId, CancellationToken.None);

        Assert.False(quit);
        Assert.Equal(original, second.Session.SessionId);
        Assert.Contains(
            second.Observer.Events.OfType<ErrorWritten>(),
            error => error.Text == SessionResume.InUseMessage(HeldId));
        second.Session.Close();
    }

    [Fact]
    public async Task Clear_ReleasesThePreviousSession()
    {
        using var holder = OpenHolder();
        await holder.Session.SubmitAsync("/clear", CancellationToken.None);

        Assert.NotEqual(HeldId, holder.Session.SessionId);
        Assert.True(new SessionStore(holder.Home).TryLock(HeldId, out var free));
        free.Dispose();
        Assert.False(new SessionStore(holder.Home).TryLock(holder.Session.SessionId, out _));
    }

    [Fact]
    public async Task Fork_OfASessionAnotherSessionHoldsTakesANewIdAndReleasesTheOldClaim()
    {
        using var holder = OpenHolder();
        var second = OpenSecond(holder, null);
        var before = second.Session.SessionId;

        await second.Session.SubmitAsync("/fork " + HeldId, CancellationToken.None);

        Assert.NotEqual(before, second.Session.SessionId);
        Assert.NotEqual(HeldId, second.Session.SessionId);
        var store = new SessionStore(holder.Home);
        Assert.True(store.TryLock(before, out var released));
        released.Dispose();
        Assert.False(store.TryLock(second.Session.SessionId, out _));
        second.Session.Close();
    }

    [Fact]
    public void Close_ReleasesTheSession()
    {
        using var holder = OpenHolder();

        holder.Session.Close();

        Assert.True(new SessionStore(holder.Home).TryLock(HeldId, out var free));
        free.Dispose();
    }

    private static HeadlessSession OpenHolder() =>
        new(new ScriptedStreamingClient(), Document());

    private static SessionDocument Document() => new()
    {
        Id = HeldId,
        Items = [new SessionItemDocument { Kind = "message", Role = "user", Text = "hello" }]
    };

    private static SessionDocument Saved(HeadlessSession holder)
    {
        var document = Document();
        document.Workspace = holder.WorkspacePath;
        return document;
    }

    private static OpenedSession OpenSecond(HeadlessSession holder, SessionDocument? resume)
    {
        if (resume is null)
        {
            // /resume and /fork read the saved file, so the held session must be on disk.
            new SessionStore(holder.Home).Save(Saved(holder));
        }

        var plugins = new PluginRegistry();
        plugins.Add(new WorkspaceToolsPlugin());
        plugins.Add(new ScriptedChatPlugin(new ScriptedStreamingClient()));
        var observer = new RecordingSessionObserver();
        var session = CodingSession.Create(
            HeadlessSession.ScriptedSettings(),
            new DiskSettingsStore(holder.Home),
            new CredentialStore(holder.Home),
            holder.Home,
            holder.WorkspacePath,
            new SessionFrontEnd(
                observer,
                new RecordingApprovalPrompt(ApprovalChoice.Deny),
                new FixedUserPrompt("unused"),
                new DecliningSessionChooser(),
                new ScriptedTrustPrompt(accept: false)),
            plugins,
            resume);
        return new OpenedSession(session, observer);
    }

    private sealed record OpenedSession(CodingSession Session, RecordingSessionObserver Observer);
}
