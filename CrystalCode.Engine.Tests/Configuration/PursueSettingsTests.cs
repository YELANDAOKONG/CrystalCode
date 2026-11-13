using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Home;

using Xunit;

namespace CrystalCode.Engine.Tests.Configuration;

public sealed class PursueSettingsTests
{
    [Fact]
    public void Limits_DefaultsAreWideAndStallsStayFinite()
    {
        var limits = PursueLimits.Default;

        Assert.Equal(1000, limits.MaximumContinuations);
        Assert.Equal(10, limits.MaximumStalls);
        Assert.Throws<ArgumentException>(() => limits.With("stalls", null));
        Assert.Throws<ArgumentException>(() => limits.With("nope", 1));
        Assert.Null(limits.With("duration", null).MaximumDurationSeconds);
        Assert.Throws<ArgumentOutOfRangeException>(() => limits.With("wait", 0));
    }

    [Fact]
    public void Store_RoundTripsModelAndLimitsAndOmitsDefaults()
    {
        using var home = new CrystalCode.Engine.Tests.Home.TemporaryHome();
        var store = new DiskSettingsStore(home.Home);
        var settings = store.LoadOrCreate();

        store.Save(settings);
        Assert.DoesNotContain("pursue", File.ReadAllText(home.Home.ConfigPath), StringComparison.Ordinal);

        var changed = settings
            .WithPursueLimits(PursueLimits.Default.With("continuations", 5).With("wait", null))
            .WithPursueModel(new PursueModelSettings(thinkingEffort: ThinkingSelection.Default));
        store.Save(changed);
        var loaded = store.LoadOrCreate();

        Assert.Equal(5, loaded.PursueLimits.MaximumContinuations);
        Assert.Null(loaded.PursueLimits.MaximumWaitSeconds);
        Assert.True(loaded.PursueModel.UsesSessionModel);
    }
}
