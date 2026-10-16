using System.Text;

using CrystalCode.Engine.Home;

using Xunit;

namespace CrystalCode.Engine.Tests.Home;

public sealed class QuickPromptStoreTests
{
    [Fact]
    public async Task LoadAsync_MissingDirectoryDoesNotCreateIt()
    {
        using var home = new TemporaryHome();
        var catalog = await new QuickPromptStore(home.Home).LoadAsync(CancellationToken.None);
        Assert.Empty(catalog.Entries);
        Assert.False(Directory.Exists(home.Home.ShortcutsDirectory));
    }

    [Fact]
    public async Task LoadAsync_ReloadsDirectMarkdownAndPreservesBody()
    {
        using var home = new TemporaryHome();
        var directory = home.Home.ShortcutsDirectory;
        Directory.CreateDirectory(Path.Combine(directory, "nested"));
        var path = Path.Combine(directory, "review.md");
        const string body = "  审查代码\r\n第二行\t\n";
        await File.WriteAllTextAsync(path, body, new UTF8Encoding(true));
        await File.WriteAllTextAsync(Path.Combine(directory, "nested", "hidden.md"), "hidden");
        await File.WriteAllTextAsync(Path.Combine(directory, "ignored.txt"), "ignored");
        var store = new QuickPromptStore(home.Home);
        var entry = Assert.Single((await store.LoadAsync(CancellationToken.None)).Entries);
        Assert.Equal("review", entry.Name);
        Assert.Equal(body, entry.Text);
        Assert.Null(entry.Error);
        await File.WriteAllTextAsync(path, "changed");
        Assert.Equal("changed", Assert.Single((await store.LoadAsync(CancellationToken.None)).Entries).Text);
        Assert.Equal(body, entry.Text);
    }

    [Fact]
    public async Task LoadAsync_InvalidFilesAreDisabledIndividually()
    {
        using var home = new TemporaryHome();
        var directory = home.Home.ShortcutsDirectory;
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "empty.md"), " \n");
        await File.WriteAllTextAsync(Path.Combine(directory, "command.md"), "\n /clear");
        await File.WriteAllTextAsync(Path.Combine(directory, "control.md"), "hello\u001b[2J");
        await File.WriteAllBytesAsync(Path.Combine(directory, "encoding.md"), [0xff, 0xfe]);
        await File.WriteAllBytesAsync(Path.Combine(directory, "large.md"), new byte[QuickPromptStore.MaximumFileBytes + 1]);
        var catalog = await new QuickPromptStore(home.Home).LoadAsync(CancellationToken.None);
        Assert.Equal(5, catalog.Entries.Count);
        Assert.All(catalog.Entries, entry => Assert.NotNull(entry.Error));
    }
}
