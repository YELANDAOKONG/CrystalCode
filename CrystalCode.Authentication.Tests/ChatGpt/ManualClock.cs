namespace CrystalCode.Authentication.Tests.ChatGpt;

internal sealed class ManualClock : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan span) => _now += span;
}
