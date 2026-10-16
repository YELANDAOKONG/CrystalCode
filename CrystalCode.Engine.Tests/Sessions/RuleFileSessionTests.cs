using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Events;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class RuleFileSessionTests
{
    [Fact]
    public async Task RulesCommand_WritesOnlyTheChangedSide()
    {
        var baseline = HeadlessSession.ScriptedSettings();
        var live = baseline.WithRuleFiles(baseline.RuleFiles.WithProject(false));
        using var headless = new HeadlessSession(
            new ScriptedStreamingClient(),
            settings: live,
            persistedSettings: baseline);
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.Session.SubmitAsync("/rules global off", CancellationToken.None);

        var saved = File.ReadAllText(headless.Home.ConfigPath);
        Assert.Contains("\"global\": false", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("\"project\"", saved, StringComparison.Ordinal);
        Assert.Contains(
            headless.Observer.Events.OfType<NoteWritten>(),
            note => note.Text.Contains("global off, project off", StringComparison.Ordinal));

        headless.Observer.Clear();
        await headless.Session.SubmitAsync("/status", CancellationToken.None);
        var status = headless.Observer.Events.OfType<StatusReported>().Single().Status;
        Assert.False(status.GlobalRulesEnabled);
        Assert.False(status.ProjectRulesEnabled);
    }

    [Fact]
    public async Task RulesCommand_RejectsAnIncompleteSide()
    {
        using var headless = new HeadlessSession(new ScriptedStreamingClient());
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.Session.SubmitAsync("/rules project", CancellationToken.None);

        Assert.Contains(
            headless.Observer.Events.OfType<ErrorWritten>(),
            error => error.Text.Contains("/rules project on|off", StringComparison.Ordinal));
        Assert.False(File.Exists(headless.Home.ConfigPath) && File.ReadAllText(headless.Home.ConfigPath).Contains("ruleFiles", StringComparison.Ordinal));
    }
}
