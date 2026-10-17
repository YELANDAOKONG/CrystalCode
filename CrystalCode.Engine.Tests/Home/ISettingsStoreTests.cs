using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Home;

using Xunit;

namespace CrystalCode.Engine.Tests.Home;

public sealed class ISettingsStoreTests
{
    [Fact]
    public void ConsumerCanUseNonDiskSettingsStore()
    {
        var settings = HarnessSettings.CreateDefault();
        ISettingsStore store = new InMemorySettingsStore(settings);

        Assert.Same(settings, store.Load());
        Assert.Same(settings, store.LoadOrCreate());

        var replacement = settings.WithEstimatedTokens(true);
        store.Save(replacement);

        Assert.Same(replacement, store.Load());
    }

    private sealed class InMemorySettingsStore : ISettingsStore
    {
        private HarnessSettings _settings;

        public InMemorySettingsStore(HarnessSettings settings)
        {
            _settings = settings;
        }

        public HarnessSettings LoadOrCreate() => _settings;

        public HarnessSettings Load() => _settings;

        public void Save(HarnessSettings settings)
        {
            _settings = settings;
        }
    }
}
