using System.Diagnostics;

using Crystal.Media;

using CrystalCode.Engine.Tests.Tools;
using CrystalCode.Engine.Tools;
using CrystalCode.Engine.Tools.External;

using Xunit;

namespace CrystalCode.Engine.Tests.Tools.External;

public sealed class ExecContentOutputTests
{
    private static readonly byte[] Png =
    [
        0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a
    ];

    [Fact]
    public async Task LoadAsync_Base64Image_ReturnsDetectedPng()
    {
        using var workspace = new TemporaryWorkspace();
        var json = "{\"text\":\"rendered\",\"images\":[{\"mimeType\":\"image/png\",\"base64\":\""
            + Convert.ToBase64String(Png)
            + "\"}]}";
        var (text, images) = await Load(json, string.Empty, new Workspace(workspace.Path));

        Assert.Equal("rendered", text);
        var image = Assert.Single(images);
        Assert.Equal("image/png", image.Image.MimeType.Value);
        Assert.Equal(Png, Assert.IsType<InlineMediaSource>(image.Image.Source).Data.ToArray());
    }

    [Fact]
    public async Task LoadAsync_WrappedBase64_IgnoresWhitespace()
    {
        using var workspace = new TemporaryWorkspace();
        var encoded = Convert.ToBase64String(Png);
        var wrapped = encoded.Insert(4, "\\n");
        var json = "{\"images\":[{\"mimeType\":\"image/png\",\"base64\":\"" + wrapped + "\"}]}";
        var (_, images) = await Load(json, string.Empty, new Workspace(workspace.Path));

        var image = Assert.Single(images);
        Assert.Equal(Png, Assert.IsType<InlineMediaSource>(image.Image.Source).Data.ToArray());
    }

    [Fact]
    public async Task LoadAsync_PathImage_ReadsWorkspaceFile()
    {
        using var workspace = new TemporaryWorkspace();
        File.WriteAllBytes(Path.Combine(workspace.Path, "chart.png"), Png);

        var (text, images) = await Load(
            """{"text":"chart","images":[{"mimeType":"image/png","path":"chart.png"}]}""",
            "warn",
            new Workspace(workspace.Path));

        Assert.Equal("chart\nwarn", text);
        Assert.Single(images);
    }

    [Fact]
    public async Task LoadAsync_OutsideAndCredentialPaths_Fail()
    {
        using var workspace = new TemporaryWorkspace();
        var root = new Workspace(workspace.Path);
        Directory.CreateDirectory(Path.Combine(workspace.Path, ".ssh"));
        File.WriteAllBytes(Path.Combine(workspace.Path, ".ssh", "id.png"), Png);

        var outside = await LoadFailure(
            """{"images":[{"mimeType":"image/png","path":"/etc/hosts"}]}""",
            root);
        Assert.Contains("outside the workspace", outside, StringComparison.Ordinal);

        var credential = await LoadFailure(
            """{"images":[{"mimeType":"image/png","path":".ssh/id.png"}]}""",
            root);
        Assert.Contains("credential", credential, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryParse_InvalidJson_KeepsStdoutPreview()
    {
        var parsed = ExecContentOutput.TryParse(
            "not-json",
            string.Empty,
            out _,
            out _,
            out var invalid);

        Assert.False(parsed);
        Assert.Contains("not valid content JSON", invalid, StringComparison.Ordinal);
        Assert.Contains("not-json", invalid, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_MimeMismatch_Fails()
    {
        using var workspace = new TemporaryWorkspace();
        var error = await LoadFailure(
            "{\"images\":[{\"mimeType\":\"image/jpeg\",\"base64\":\""
                + Convert.ToBase64String(Png)
                + "\"}]}",
            new Workspace(workspace.Path));

        Assert.Contains("do not match mimeType", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParse_TooManyImages_Fails()
    {
        var encoded = Convert.ToBase64String(Png);
        var images = string.Join(
            ",",
            Enumerable.Range(0, ExecContentOutput.MaximumImages + 1)
                .Select(_ => "{\"mimeType\":\"image/png\",\"base64\":\"" + encoded + "\"}"));

        var parsed = ExecContentOutput.TryParse(
            "{\"images\":[" + images + "]}",
            string.Empty,
            out _,
            out _,
            out var error);

        Assert.False(parsed);
        Assert.Contains("at most", error, StringComparison.Ordinal);
    }

    [Fact(Timeout = 5000)]
    public async Task LoadAsync_Fifo_FailsWithoutOpening()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        using var workspace = new TemporaryWorkspace();
        var pipe = Path.Combine(workspace.Path, "shot.pipe");
        using var created = Process.Start(new ProcessStartInfo
        {
            FileName = "mkfifo",
            ArgumentList = { pipe },
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });
        Assert.NotNull(created);
        Assert.True(created.WaitForExit(TimeSpan.FromSeconds(2)));
        Assert.Equal(0, created.ExitCode);

        var error = await LoadFailure(
            """{"images":[{"mimeType":"image/png","path":"shot.pipe"}]}""",
            new Workspace(workspace.Path));

        Assert.Contains("could not be read", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_Cancelled_Throws()
    {
        using var workspace = new TemporaryWorkspace();
        File.WriteAllBytes(Path.Combine(workspace.Path, "chart.png"), Png);
        Assert.True(ExecContentOutput.TryParse(
            """{"images":[{"mimeType":"image/png","path":"chart.png"}]}""",
            string.Empty,
            out _,
            out var pending,
            out var error), error);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ExecContentOutput.LoadAsync(
                pending,
                new Workspace(workspace.Path),
                cancellation.Token).AsTask());
    }

    private static async Task<(string Text, IReadOnlyList<Crystal.Multimodal.ImageContent> Images)> Load(
        string stdout,
        string stderr,
        Workspace workspace)
    {
        Assert.True(
            ExecContentOutput.TryParse(stdout, stderr, out var text, out var pending, out var error),
            error);
        var loaded = await ExecContentOutput.LoadAsync(pending, workspace, CancellationToken.None);
        Assert.True(loaded.Succeeded, loaded.Error);
        return (text, loaded.Images);
    }

    private static async Task<string> LoadFailure(string stdout, Workspace workspace)
    {
        Assert.True(
            ExecContentOutput.TryParse(stdout, string.Empty, out _, out var pending, out var error),
            error);
        var loaded = await ExecContentOutput.LoadAsync(pending, workspace, CancellationToken.None);
        Assert.False(loaded.Succeeded);
        return loaded.Error;
    }
}
