using Xunit;

using CrystalCode.Engine.Home;
using CrystalCode.Engine.Sessions;

namespace CrystalCode.Engine.Tests.Home;

public sealed class PromptHistoryStoreTests
{
    [Fact]
    public async Task AppendAsync_KeepsTypedMarkersButSkipsAttachments()
    {
        using var temporary = new TemporaryHome();
        var first = new PromptHistoryStore(temporary.Home, "/workspace/first");
        var second = new PromptHistoryStore(temporary.Home, "/workspace/second");

        await first.AppendAsync("first prompt", CancellationToken.None);
        await first.AppendAsync("first prompt", CancellationToken.None);
        await first.AppendAsync("image [Image #1]", CancellationToken.None);
        await first.AppendAsync(
            "attached " + ImageMarkerText.Prefix + "[Image #1]",
            CancellationToken.None);
        await second.AppendAsync("second prompt", CancellationToken.None);

        Assert.Equal(
            ["first prompt", "image [Image #1]"],
            await first.LoadAsync(CancellationToken.None));
        Assert.Equal(["second prompt"], await second.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AppendAsync_KeepsTwoHundredEntriesForEachWorkspace()
    {
        using var temporary = new TemporaryHome();
        var first = new PromptHistoryStore(temporary.Home, "/workspace/first");
        var second = new PromptHistoryStore(temporary.Home, "/workspace/second");
        await second.AppendAsync("keep", CancellationToken.None);
        for (var index = 0; index < 200; index++)
        {
            await first.AppendAsync($"prompt-{index}", CancellationToken.None);
        }

        await first.AppendAsync("newest", CancellationToken.None);

        var loaded = await first.LoadAsync(CancellationToken.None);
        Assert.Equal(200, loaded.Count);
        Assert.Equal("prompt-1", loaded[0]);
        Assert.Equal("newest", loaded[^1]);
        Assert.Equal(["keep"], await second.LoadAsync(CancellationToken.None));
    }
}
