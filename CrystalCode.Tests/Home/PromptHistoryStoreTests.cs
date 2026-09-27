using Xunit;

using CrystalCode.Home;

namespace CrystalCode.Tests.Home;

public sealed class PromptHistoryStoreTests
{
    [Fact]
    public async Task AppendAsync_RestoresWorkspaceTextWithoutImageMarkers()
    {
        using var temporary = new TemporaryHome();
        var first = new PromptHistoryStore(temporary.Home, "/workspace/first");
        var second = new PromptHistoryStore(temporary.Home, "/workspace/second");

        await first.AppendAsync("first prompt", CancellationToken.None);
        await first.AppendAsync("first prompt", CancellationToken.None);
        await first.AppendAsync("image [Image #1]", CancellationToken.None);
        await second.AppendAsync("second prompt", CancellationToken.None);

        Assert.Equal(["first prompt"], await first.LoadAsync(CancellationToken.None));
        Assert.Equal(["second prompt"], await second.LoadAsync(CancellationToken.None));
    }
}
