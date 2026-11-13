using Crystal;
using Crystal.Chat;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Sessions.Pursue;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class CodingSessionPursueTests
{
    [Fact]
    public async Task Start_RunsOneSegmentAndStopsWhenTheCheckerSaysDone()
    {
        var model = new ScriptedStreamingClient(Text("All finished."), Text(Done(2)));
        using var headless = new HeadlessSession(model);
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.RunTurnAsync("/pursue start fix the build");

        Assert.Equal(2, model.RequestCount);
        var state = LastState(headless);
        Assert.Equal(PursueStatus.Complete, state.Status);
        Assert.Equal(PursueReasonCodes.Done, state.ReasonCode);
        Assert.Equal(1, state.Checks);
        var checkEvent = Assert.Single(headless.Observer.Events.OfType<PursueChecked>());
        Assert.Equal("done", checkEvent.Verdict);
        Assert.Equal([2], checkEvent.Evidence);
        Assert.Single(headless.Observer.Events.OfType<TurnStarted>());
        var finished = Assert.Single(headless.Observer.Events.OfType<TurnFinished>());
        Assert.Equal(TurnStopReason.Completed, finished.Result.StopReason);
        Assert.Equal(1, finished.Result.ModelCallCount);
        var saved = headless.ReadSaved().Pursue!;
        Assert.Equal("complete", saved.Status);
        Assert.Equal("fix the build", saved.Objective);
    }

    [Fact]
    public async Task Continue_SendsTheWorkingModelOnAgainAsOneLogicalTurn()
    {
        var model = new ScriptedStreamingClient(
            Text("Working."),
            Text(Continue("Run the tests.", "The tests have not run.")),
            Text("Tests pass."),
            Text(Done(4)));
        using var headless = new HeadlessSession(model);
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.RunTurnAsync("/pursue start make the tests pass");

        Assert.Equal(4, model.RequestCount);
        var state = LastState(headless);
        Assert.Equal(PursueStatus.Complete, state.Status);
        Assert.Equal(1, state.Continuations);
        Assert.Equal(2, state.Checks);
        Assert.Single(headless.Observer.Events.OfType<TurnStarted>());
        var finished = Assert.Single(headless.Observer.Events.OfType<TurnFinished>());
        Assert.Equal(2, finished.Result.ModelCallCount);
        var archive = headless.ReadSaved().Archive!;
        Assert.Contains(
            archive,
            item => item.Text is not null && item.Text.Contains("automatic continuation", StringComparison.Ordinal)
                && item.Text.Contains("Run the tests.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Blocked_StopsAndResumeContinuesFromTheSavedObjective()
    {
        var model = new ScriptedStreamingClient(
            Text("I need a key."),
            Text("""{"verdict":"blocked","reason":"An API key is missing."}"""),
            Text("Used the key."),
            Text(Done(4)));
        using var headless = new HeadlessSession(model);
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.RunTurnAsync("/pursue start call the service");

        var blocked = LastState(headless);
        Assert.Equal(PursueStatus.Blocked, blocked.Status);
        Assert.Equal("An API key is missing.", blocked.Reason);
        Assert.Equal(
            TurnStopReason.PursueBlocked,
            headless.Observer.Events.OfType<TurnFinished>().Single().Result.StopReason);

        await headless.RunTurnAsync("/pursue resume");

        Assert.Equal(PursueStatus.Complete, LastState(headless).Status);
        Assert.Equal(2, headless.Observer.Events.OfType<TurnStarted>().Count());
        Assert.Equal(4, model.RequestCount);
    }

    [Fact]
    public async Task InvalidVerdict_PausesWithoutAnotherRequestAndWritesAnError()
    {
        var model = new ScriptedStreamingClient(Text("Done."), Text("looks good to me"));
        using var headless = new HeadlessSession(model);
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.RunTurnAsync("/pursue start finish");

        Assert.Equal(2, model.RequestCount);
        var state = LastState(headless);
        Assert.Equal(PursueStatus.Paused, state.Status);
        Assert.Equal(PursueReasonCodes.ReviewInvalid, state.ReasonCode);
        Assert.Contains(headless.Observer.Events.OfType<ErrorWritten>(), error => error.Text.Contains("verdict", StringComparison.Ordinal));
        Assert.Equal(
            TurnStopReason.Failed,
            headless.Observer.Events.OfType<TurnFinished>().Single().Result.StopReason);
    }

    [Fact]
    public async Task DoneWithoutEvidence_IsInvalid()
    {
        var model = new ScriptedStreamingClient(
            Text("Done."),
            Text("""{"verdict":"done","reason":"Looks complete."}"""));
        using var headless = new HeadlessSession(model);
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.RunTurnAsync("/pursue start finish");

        Assert.Equal(PursueReasonCodes.ReviewInvalid, LastState(headless).ReasonCode);
    }

    [Fact]
    public async Task Stalls_PauseTheRunAtTheLimit()
    {
        var model = new ScriptedStreamingClient(
            Text("Thinking."),
            Text(Continue("Try again.", "Nothing was done.")));
        var settings = HeadlessSession.ScriptedSettings().WithPursueLimits(new PursueLimits(maximumStalls: 1));
        using var headless = new HeadlessSession(model, settings: settings);
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.RunTurnAsync("/pursue start do it");

        var state = LastState(headless);
        Assert.Equal(PursueStatus.Paused, state.Status);
        Assert.Equal(PursueReasonCodes.Stalled, state.ReasonCode);
        Assert.Equal(
            TurnStopReason.PursueLimitReached,
            headless.Observer.Events.OfType<TurnFinished>().Single().Result.StopReason);
    }

    [Fact]
    public async Task ContinuationLimit_PausesBeforeTheExtraSegment()
    {
        var model = new ScriptedStreamingClient(Text("Part one."), Text(Continue("Part two.", "More to do.")));
        var settings = HeadlessSession.ScriptedSettings().WithPursueLimits(new PursueLimits(maximumContinuations: 0));
        using var headless = new HeadlessSession(model, settings: settings);
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.RunTurnAsync("/pursue start do it");

        Assert.Equal(2, model.RequestCount);
        Assert.Equal(PursueReasonCodes.ContinuationLimit, LastState(headless).ReasonCode);
    }

    [Fact]
    public async Task ModelCallLimit_CountsTheCheckerAgainstTheRun()
    {
        var model = new ScriptedStreamingClient(Text("Working."), Text(Continue("More.", "Not done.")));
        var settings = HeadlessSession.ScriptedSettings().WithPursueLimits(new PursueLimits(maximumModelCalls: 2));
        using var headless = new HeadlessSession(model, settings: settings);
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.RunTurnAsync("/pursue start do it");

        var state = LastState(headless);
        Assert.Equal(PursueReasonCodes.ModelLimit, state.ReasonCode);
        Assert.Equal(2, model.RequestCount);
    }

    [Fact]
    public async Task Clear_RemovesTheObjectiveAndPauseKeepsItResumable()
    {
        var model = new ScriptedStreamingClient(Text("Stuck."), Text("not json"));
        using var headless = new HeadlessSession(model);
        await headless.Session.StartAsync(CancellationToken.None);
        await headless.RunTurnAsync("/pursue start finish");

        await headless.RunTurnAsync("/pursue pause");
        Assert.Equal(PursueReasonCodes.UserPaused, LastState(headless).ReasonCode);
        Assert.Equal("paused", headless.ReadSaved().Pursue!.Status);

        await headless.RunTurnAsync("/pursue clear");
        Assert.Null(headless.Observer.Events.OfType<PursueChanged>().Last().Pursue);
        Assert.Null(headless.ReadSaved().Pursue);
    }

    [Fact]
    public async Task Commands_RejectBadInputAndWriteLimits()
    {
        var model = new ScriptedStreamingClient();
        using var headless = new HeadlessSession(model);
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.RunTurnAsync("/pursue start");
        await headless.RunTurnAsync("/pursue pause");
        await headless.RunTurnAsync("/pursue resume");
        await headless.RunTurnAsync("/pursue limit stalls unlimited");
        await headless.RunTurnAsync("/pursue limit continuations 5");
        await headless.RunTurnAsync("/pursue tier flex");

        var errors = headless.Observer.Events.OfType<ErrorWritten>().Select(static error => error.Text).ToArray();
        Assert.Equal(5, errors.Length);
        Assert.Contains(errors, text => text.Contains("start <objective>", StringComparison.Ordinal));
        Assert.Contains(errors, text => text.Contains("cannot be unlimited", StringComparison.Ordinal));
        Assert.Contains(errors, text => text.Contains("own model", StringComparison.Ordinal));
        Assert.Contains(
            headless.Observer.Events.OfType<NoteWritten>(),
            note => note.Text.Contains("continuations 5", StringComparison.Ordinal));
        Assert.Equal(0, model.RequestCount);
    }

    [Fact]
    public async Task Resume_ReadsAnActiveSavedObjectiveAsPaused()
    {
        var document = new SessionDocument
        {
            Pursue = new PursueDocument { Objective = "ship it", Status = "active" }
        };
        var model = new ScriptedStreamingClient();
        using var headless = new HeadlessSession(model, resume: document);

        await headless.Session.StartAsync(CancellationToken.None);

        var state = LastState(headless);
        Assert.Equal("ship it", state.Objective);
        Assert.Equal(PursueStatus.Paused, state.Status);
        Assert.Equal(PursueReasonCodes.Restarted, state.ReasonCode);
        Assert.Equal(0, model.RequestCount);
    }

    [Fact]
    public async Task OwnModel_ChecksOnItsOwnClientWithItsOwnSlots()
    {
        var model = new ScriptedStreamingClient(Text("Finished."), Text(Done(2)));
        var provider = new ProviderName("scripted");
        var definition = new ProviderDefinition(
            provider,
            ScriptedChatPlugin.Protocol,
            new Uri("http://localhost:11434/"),
            new Dictionary<string, ModelSettings>(StringComparer.Ordinal)
            {
                ["model"] = new(200_000, serviceTiers: ["slow"]),
                ["checker"] = new(200_000, serviceTiers: ["flex"], speeds: ["fast"])
            },
            requiresApiKey: false);
        var settings = new HarnessSettings(
                provider,
                "model",
                ApprovalMode.Default,
                0.8,
                ProviderCatalog.CreateStarter().Overlay([definition]))
            .WithPursueModel(new PursueModelSettings("scripted", "checker", serviceTier: "flex"));
        using var headless = new HeadlessSession(model, settings: settings);
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.RunTurnAsync("/pursue start finish");

        Assert.Equal(PursueStatus.Complete, LastState(headless).Status);
        Assert.Contains(("checker", (string?)"flex", (string?)null), headless.ChatPlugin.Slots);
    }

    [Fact]
    public async Task Compaction_IsConfiguredSeparatelyAndSurvivesAModelChange()
    {
        var provider = new ProviderName("scripted");
        var definition = new ProviderDefinition(
            provider,
            ScriptedChatPlugin.Protocol,
            new Uri("http://localhost:11434/"),
            new Dictionary<string, ModelSettings>(StringComparer.Ordinal)
            {
                ["model"] = new(200_000),
                ["summary"] = new(200_000, thinking: true, thinkingEfforts: ["high"])
            },
            requiresApiKey: false);
        var settings = new HarnessSettings(
            provider,
            "model",
            ApprovalMode.Default,
            0.8,
            ProviderCatalog.CreateStarter().Overlay([definition]));
        using var headless = new HeadlessSession(
            new ScriptedStreamingClient(),
            settings: settings,
            prepare: (home, _) =>
            {
                home.EnsureCreated();
                File.WriteAllText(home.ProvidersPath, System.Text.Json.JsonSerializer.Serialize(SettingsMapper.WriteProviders(settings.Catalog)));
            });
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.RunTurnAsync("/pursue compaction thinking high");
        Assert.Single(headless.Observer.Events.OfType<ErrorWritten>());

        await headless.RunTurnAsync("/pursue compaction model scripted summary");
        await headless.RunTurnAsync("/pursue compaction thinking high");
        await headless.RunTurnAsync("/pursue model scripted model");

        var stored = new DiskSettingsStore(headless.Home).Load().PursueModel;
        Assert.Equal("summary", stored.Compaction.Model);
        Assert.Equal("high", stored.Compaction.ThinkingEffort.Value);

        await headless.RunTurnAsync("/pursue compaction model");
        Assert.False(new DiskSettingsStore(headless.Home).Load().PursueModel.Compaction.HasSelection);
    }

    [Fact]
    public void Model_ChangeClearsTheSlotsOfThePreviousSelection()
    {
        var own = new PursueModelSettings("scripted", "checker", serviceTier: "flex", speed: "fast", contextWindow: 1000);

        var other = own.WithModel("scripted", "other");
        var session = own.WithModel(null, null);

        Assert.Null(other.ServiceTier);
        Assert.Null(other.Speed);
        Assert.Null(other.ContextWindow);
        Assert.True(session.UsesSessionModel);
        Assert.True(session.IsDefault);
    }

    [Fact]
    public async Task History_RetainsEvidenceAcrossResumeAndPersistenceAndClearsWithTheObjective()
    {
        var model = new ScriptedStreamingClient(
            Text("Need access."), Text("""{"verdict":"blocked","reason":"Missing access."}"""),
            Text("Tests pass."), Text(Done(4)));
        using var headless = new HeadlessSession(model);
        await headless.Session.StartAsync(CancellationToken.None);
        await headless.RunTurnAsync("/pursue start fix it");
        var first = Assert.Single(headless.Session.ListPursueChecks().Checks);
        await headless.RunTurnAsync("/pursue resume");
        var history = headless.Session.ListPursueChecks();
        Assert.Equal(2, history.Checks.Count);
        var last = history.Checks[1];
        Assert.Equal(first.ObjectiveId, last.ObjectiveId);
        Assert.NotEqual(first.RunId, last.RunId);
        Assert.NotEqual(first.Id, last.Id);
        Assert.Equal(1, last.Index);
        Assert.Contains("Tests pass.", Assert.Single(last.Evidence).Text);
        Assert.Equal(last, headless.Session.GetPursueCheck(last.Id));
        var page = headless.Session.ListPursueChecks(limit: 1);
        Assert.True(page.HasMore);
        Assert.Equal(last, Assert.Single(headless.Session.ListPursueChecks(page.NextOffset).Checks));

        using var restored = new HeadlessSession(new ScriptedStreamingClient(), resume: headless.ReadSaved());
        await restored.Session.StartAsync(CancellationToken.None);
        var restoredCheck = restored.Session.GetPursueCheck(last.Id)!;
        Assert.Equal(last.Evidence, restoredCheck.Evidence);
        Assert.Equal(last.Usage, restoredCheck.Usage);
        var state = new SessionState();
        foreach (var item in restored.Observer.Events)
        {
            state.Apply(item);
        }

        Assert.Equal(last.Id, state.Snapshot().Pursue!.History.Last().Id);
        await restored.RunTurnAsync("/pursue clear");
        Assert.Null(restored.Session.GetPursue());
        Assert.Empty(restored.Session.ListPursueChecks().Checks);
    }

    [Fact]
    public async Task History_InvalidVerdictIsSavedAndNewStartReplacesIt()
    {
        using var headless = new HeadlessSession(new ScriptedStreamingClient(
            Text("Done."), Text("invalid"), Text("Finished."), Text(Done(4))));
        await headless.Session.StartAsync(CancellationToken.None);
        await headless.RunTurnAsync("/pursue start first");
        var failed = Assert.Single(headless.ReadSaved().Pursue!.History);
        Assert.Equal("failed", failed.Verdict);
        Assert.Contains("unusable verdict", failed.Reason);
        await headless.RunTurnAsync("/pursue start second");
        Assert.Null(headless.Session.GetPursueCheck(failed.Id));
        Assert.NotEqual(failed.ObjectiveId, headless.Session.GetPursue()!.Id);
        Assert.Single(headless.Session.ListPursueChecks().Checks);
    }

    private static PursueState LastState(HeadlessSession headless) =>
        headless.Observer.Events.OfType<PursueChanged>().Last().Pursue!;

    private static string Done(int entry) =>
        "{\"verdict\":\"done\",\"reason\":\"The objective is met.\",\"evidence\":[" + entry + "]}";

    private static string Continue(string nextStep, string reason) =>
        "{\"verdict\":\"continue\",\"reason\":\"" + reason + "\",\"nextStep\":\"" + nextStep + "\"}";

    private static IReadOnlyList<ChatStreamEvent> Text(string text) =>
    [
        new ChatTextDelta(0, 0, ChatRole.Assistant, text),
        new ChatUsageReceived(new TokenUsage(10, 2, 1)),
        new ChatCandidateCompleted(0, FinishReason.Stop)
    ];
}
