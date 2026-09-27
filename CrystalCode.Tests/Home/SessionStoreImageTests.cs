using System.Text.Json;

using CrystalCode.Home;
using CrystalCode.Sessions;

using Xunit;

namespace CrystalCode.Tests.Home;

public sealed class SessionStoreImageTests
{
    private static readonly byte[] Png =
        [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 1, 2, 3, 4];

    [Fact]
    public void Save_StoresImageOutsideSessionJsonAndRestoresIt()
    {
        using var root = new TemporaryHome();
        var store = new SessionStore(root.Home);
        store.Save(CreateSession());

        using var saved = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root.Home.SessionsDirectory, "image-session.json")));
        var image = saved.RootElement.GetProperty("images")[0];
        Assert.False(image.TryGetProperty("data", out _));
        var hash = image.GetProperty("contentHash").GetString();
        Assert.NotNull(hash);
        Assert.Equal(Png, File.ReadAllBytes(Path.Combine(root.Home.MediaDirectory, hash)));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(Path.Combine(root.Home.MediaDirectory, hash)));
        }

        Assert.True(store.TryLoad("image-session", out var loaded));
        Assert.Equal(Png, store.ReadImages(loaded.Images)[1].Data?.ToArray());
        Assert.Equal(2, store.ReadImages(loaded.Images).Count);
    }

    [Fact]
    public void MissingImage_DoesNotPreventSessionLoadOrEraseReferenceOnSave()
    {
        using var root = new TemporaryHome();
        var store = new SessionStore(root.Home);
        store.Save(CreateSession());
        Assert.True(store.TryLoad("image-session", out var original));
        var hash = original.Images[0].ContentHash;
        Assert.NotNull(hash);
        File.Delete(Path.Combine(root.Home.MediaDirectory, hash));

        Assert.True(store.TryLoad("image-session", out var loaded));
        Assert.True(SessionResume.TryLoad(
            store,
            "/tmp/workspace",
            "image-session",
            out _,
            out _));
        Assert.Single(store.ReadImages(loaded.Images));
        store.Save(loaded);
        Assert.True(store.TryLoad("image-session", out var saved));
        Assert.Equal(hash, saved.Images[0].ContentHash);
        Assert.Null(saved.Images[0].Data);
    }

    [Fact]
    public void TruncatedImage_DoesNotPreventSessionLoad()
    {
        using var root = new TemporaryHome();
        var store = new SessionStore(root.Home);
        store.Save(CreateSession());
        Assert.True(store.TryLoad("image-session", out var original));
        var hash = original.Images[0].ContentHash;
        Assert.NotNull(hash);
        File.WriteAllBytes(Path.Combine(root.Home.MediaDirectory, hash), Png[..8]);

        Assert.True(store.TryLoad("image-session", out var loaded));
        Assert.Single(store.ReadImages(loaded.Images));
        Assert.Equal(2, loaded.Images.Count);

        store.Save(CreateSession());
        Assert.Equal(Png, File.ReadAllBytes(Path.Combine(root.Home.MediaDirectory, hash)));
    }

    [Fact]
    public void InlineDataImage_IsReadableAndMovesToBinaryStoreOnSave()
    {
        using var root = new TemporaryHome();
        var store = new SessionStore(root.Home);
        root.Home.EnsureCreated();
        var inlineSession = CreateSession();
        File.WriteAllText(
            Path.Combine(root.Home.SessionsDirectory, "image-session.json"),
            JsonSerializer.Serialize(inlineSession, HomeJson.Options));

        Assert.True(store.TryLoad("image-session", out var loaded));
        Assert.Equal(Png, store.ReadImages(loaded.Images)[1].Data?.ToArray());
        store.Save(loaded);
        Assert.True(store.TryLoad("image-session", out var saved));
        Assert.Null(saved.Images[0].Data);
        Assert.NotNull(saved.Images[0].ContentHash);
        Assert.Equal(Png, store.ReadImages(saved.Images)[1].Data?.ToArray());
    }

    [Fact]
    public void OversizedSource_IsRejectedBeforeMediaStoreIsCreated()
    {
        using var root = new TemporaryHome();
        Directory.CreateDirectory(root.Root);
        var path = Path.Combine(root.Root, "huge.png");
        using (var stream = File.Create(path))
        {
            stream.SetLength(ImageFile.MaximumBytes + 1L);
        }

        Assert.Throws<InvalidDataException>(() => ImageFile.Load(path, 1));
        Assert.False(Directory.Exists(root.Home.MediaDirectory));
    }

    [Fact]
    public void InvalidImageData_IsRejectedBeforeMediaStoreIsCreated()
    {
        using var root = new TemporaryHome();
        var store = new ImageBlobStore(root.Home);

        Assert.Throws<InvalidDataException>(() => store.Store(Png, "image/jpeg"));
        Assert.False(Directory.Exists(root.Home.MediaDirectory));
    }

    private static SessionDocument CreateSession() => new()
    {
        Id = "image-session",
        Workspace = "/tmp/workspace",
        Items =
        [
            new SessionItemDocument
            {
                Kind = "message",
                Role = "user",
                Text = "Look at [Image #1] and [Image #2]"
            }
        ],
        Images =
        [
            new SessionImageDocument { Number = 1, MimeType = "image/png", Data = Png },
            new SessionImageDocument
            {
                Number = 2,
                MimeType = "image/webp",
                Uri = "https://example.test/image.webp"
            }
        ]
    };
}
