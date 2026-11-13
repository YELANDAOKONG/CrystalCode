using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Sessions.Pursue;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class PursueReportTests
{
    [Fact]
    public async Task Status_ReportsDefaultsAsSeparateRowsWithoutModelCalls()
    {
        var model = new ScriptedStreamingClient();
        using var headless = new HeadlessSession(model);
        await headless.Session.StartAsync(CancellationToken.None);
        await headless.RunTurnAsync("/pursue status");
        var document = Assert.Single(headless.Observer.Events.OfType<DocumentWritten>()).Document;
        var rows = document.Blocks.SelectMany(block => block.Rows).ToDictionary(row => row[0], row => row[1]);
        Assert.Equal("Pursue", document.Title);
        Assert.Equal("No objective", rows["Status"]);
        Assert.Equal("Session model", rows["Checker model"]);
        Assert.Equal("1,000", rows["Continuation limit"]);
        Assert.Equal("100,000", rows["Tool call limit"]);
        Assert.Equal("7 days", rows["Duration limit"]);
        Assert.Equal("3 hours", rows["Wait limit"]);
        Assert.Equal(0, model.RequestCount);
    }

    [Fact]
    public void Build_PreservesLongObjectivesCountersAndExactDurations()
    {
        var objective = new string('x', PursueState.MaximumObjectiveCharacters);
        var state = new PursueState(objective, PursueStatus.Blocked, Reason: "Access needed.",
            Checks: 12, Continuations: 3, Stalls: 1, NextStep: "Provide access.");
        var limits = PursueLimits.Default.With("duration", 90061).With("wait", null).With("continuations", 0);
        var document = PursueReport.Build(state, new PursueConfiguration(PursueModelSettings.Default, limits));
        var rows = document.Blocks.SelectMany(block => block.Rows).ToDictionary(row => row[0], row => row[1]);
        Assert.Equal(objective, rows["Objective"]);
        Assert.Equal("Access needed.", rows["Reason"]);
        Assert.Equal("Provide access.", rows["Next step"]);
        Assert.Equal("12", rows["Checks this run"]);
        Assert.Equal("0", rows["Continuation limit"]);
        Assert.Equal("1 day 1 hour 1 minute 1 second", rows["Duration limit"]);
        Assert.Equal("Unlimited", rows["Wait limit"]);
    }
}
