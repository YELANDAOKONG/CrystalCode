using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class SessionRetryStatusTests
{
    [Fact]
    public async Task Status_ReportsRetryPoliciesFromSettings()
    {
        var settings = HeadlessSession.ScriptedSettings()
            .WithRetry(new RetrySettings(maximumRetries: 3, initialDelaySeconds: 1.5, jitterFactor: 0))
            .WithApprovalRetry(new RetrySettings(enabled: false));
        using var headless = new HeadlessSession(new ScriptedStreamingClient(), settings: settings);
        await headless.Session.StartAsync(CancellationToken.None);

        headless.Observer.Clear();
        var quit = await headless.Session.SubmitAsync("/status", CancellationToken.None);

        Assert.False(quit);
        var status = headless.Observer.Events.OfType<StatusReported>().Single().Status;
        Assert.Equal("3 retries, 1.5s base x2, 0% jitter, 30s cap", status.Retry);
        Assert.Equal("Disabled", status.ApprovalRetry);
    }
}
