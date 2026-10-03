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
    public void TryRead_Base64Image_ReturnsDetectedPng()
    {
        using var workspace = new TemporaryWorkspace();
        var json = "{\"text\":\"rendered\",\"images\":[{\"mimeType\":\"image/png\",\"base64\":\""
            + Convert.ToBase64String(Png)
            + "\"}]}";

        var parsed = ExecContentOutput.TryRead(
            json,
            string.Empty,
            new Workspace(workspace.Path),
            out var text,
            out var images,
            out var error);

        Assert.True(parsed, error);
        Assert.Equal("rendered", text);
        var image = Assert.Single(images);
        Assert.Equal("image/png", image.Image.MimeType.Value);
        Assert.Equal(Png, Assert.IsType<Crystal.Media.InlineMediaSource>(image.Image.Source).Data.ToArray());
    }

    [Fact]
    public void TryRead_PathImage_ReadsWorkspaceFile()
    {
        using var workspace = new TemporaryWorkspace();
        File.WriteAllBytes(Path.Combine(workspace.Path, "chart.png"), Png);

        var parsed = ExecContentOutput.TryRead(
            """{"text":"chart","images":[{"mimeType":"image/png","path":"chart.png"}]}""",
            "warn",
            new Workspace(workspace.Path),
            out var text,
            out var images,
            out var error);

        Assert.True(parsed, error);
        Assert.Equal("chart\nwarn", text);
        Assert.Single(images);
    }

    [Fact]
    public void TryRead_OutsideAndCredentialPaths_Fail()
    {
        using var workspace = new TemporaryWorkspace();
        var root = new Workspace(workspace.Path);
        Directory.CreateDirectory(Path.Combine(workspace.Path, ".ssh"));
        File.WriteAllBytes(Path.Combine(workspace.Path, ".ssh", "id.png"), Png);

        var outside = ExecContentOutput.TryRead(
            """{"images":[{"mimeType":"image/png","path":"/etc/hosts"}]}""",
            string.Empty,
            root,
            out _,
            out _,
            out var outsideError);
        Assert.False(outside);
        Assert.Contains("outside the workspace", outsideError, StringComparison.Ordinal);

        var credential = ExecContentOutput.TryRead(
            """{"images":[{"mimeType":"image/png","path":".ssh/id.png"}]}""",
            string.Empty,
            root,
            out _,
            out _,
            out var credentialError);
        Assert.False(credential);
        Assert.Contains("credential", credentialError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryRead_InvalidJsonAndMimeMismatch_Fail()
    {
        using var workspace = new TemporaryWorkspace();
        var root = new Workspace(workspace.Path);

        Assert.False(ExecContentOutput.TryRead(
            "not-json",
            string.Empty,
            root,
            out _,
            out _,
            out var invalid));
        Assert.Contains("not valid content JSON", invalid, StringComparison.Ordinal);

        var mismatch = ExecContentOutput.TryRead(
            "{\"images\":[{\"mimeType\":\"image/jpeg\",\"base64\":\""
                + Convert.ToBase64String(Png)
                + "\"}]}",
            string.Empty,
            root,
            out _,
            out _,
            out var mismatchError);
        Assert.False(mismatch);
        Assert.Contains("do not match mimeType", mismatchError, StringComparison.Ordinal);
    }

    [Fact]
    public void TryRead_TooManyImages_Fails()
    {
        using var workspace = new TemporaryWorkspace();
        var encoded = Convert.ToBase64String(Png);
        var images = string.Join(
            ",",
            Enumerable.Range(0, ExecContentOutput.MaximumImages + 1)
                .Select(_ => "{\"mimeType\":\"image/png\",\"base64\":\"" + encoded + "\"}"));

        var parsed = ExecContentOutput.TryRead(
            "{\"images\":[" + images + "]}",
            string.Empty,
            new Workspace(workspace.Path),
            out _,
            out _,
            out var error);

        Assert.False(parsed);
        Assert.Contains("at most", error, StringComparison.Ordinal);
    }
}
