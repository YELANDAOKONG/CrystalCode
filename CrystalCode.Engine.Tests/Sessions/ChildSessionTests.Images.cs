using System.Text.Json;

using Crystal.Multimodal;
using Crystal.Multimodal.Tools;

using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed partial class ChildSessionTests
{
    [Fact]
    public async Task VisionChild_ReceivesOnlyItsOwnImagesAndPersistsThemInParentMediaStore()
    {
        var provider = new ProviderDefinition(new ProviderName("scripted"), ScriptedChatPlugin.Protocol,
            new Uri("http://localhost:11434/"), new Dictionary<string, ModelSettings>
            {
                ["model"] = new(200_000), ["vision"] = new(200_000, imageInput: true)
            });
        var settings = new HarnessSettings(provider.Name, "model", Engine.Approvals.ApprovalMode.Default, 0.8,
            new ProviderCatalog([provider]), agents: new AgentSettings { Enabled = true,
                Explore = new AgentModelSettings { Provider = "scripted", Model = "vision" } });
        var text = new ScriptedStreamingClient(
            Tool("task", """{"agent":"explore","label":"look","prompt":"Inspect sample.png"}"""), Text("parent"));
        var vision = new ChildVisionClient();
        using var session = new HeadlessSession(text, settings: settings, images: vision);
        await File.WriteAllBytesAsync(Path.Combine(session.WorkspacePath, "sample.png"),
            [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
        await session.Session.StartAsync(CancellationToken.None);
        await session.RunTurnAsync("delegate image inspection");
        var saved = session.ReadSaved();
        var child = Assert.Single(saved.Children);
        Assert.Equal("completed", child.Status);
        Assert.Equal("vision", child.Model.Model);
        Assert.Contains("view_image", child.Tools);
        Assert.DoesNotContain(text.LastRequest!.Tools, tool => tool.Name == "view_image");
        Assert.Equal(2, vision.Requests.Count);
        Assert.Contains(Assert.Single(vision.Requests[1].Items.OfType<MultimodalToolResult>()).Contents,
            content => content is ImageContent);
        Assert.Empty(saved.Images);
        var image = Assert.Single(child.Images);
        Assert.NotNull(image.ContentHash);
        Assert.Null(image.Data);
        Assert.Equal(30, child.CumulativeUsage!.InputTokenCount);
        Assert.Equal(60, saved.CumulativeUsage!.InputTokenCount);
        var exportPath = Path.Combine(session.WorkspacePath, "child-export.json");
        await session.Session.SubmitAsync("/export json \"" + exportPath + "\"", CancellationToken.None);
        using var exported = JsonDocument.Parse(await File.ReadAllTextAsync(exportPath));
        var exportedImage = exported.RootElement.GetProperty("session").GetProperty("children")[0].GetProperty("images")[0];
        Assert.NotNull(exportedImage.GetProperty("data").GetString());
    }

    [Fact]
    public async Task TextChild_HasNoViewImageEvenWhenAudioAndVideoAreDeclared()
    {
        var provider = new ProviderDefinition(new ProviderName("scripted"), ScriptedChatPlugin.Protocol,
            new Uri("http://localhost:11434/"), new Dictionary<string, ModelSettings>
            { ["model"] = new(200_000, audioInput: true, videoInput: true) });
        var settings = new HarnessSettings(provider.Name, "model", Engine.Approvals.ApprovalMode.Default, 0.8,
            new ProviderCatalog([provider]), agents: new AgentSettings { Enabled = true });
        using var session = new HeadlessSession(new ScriptedStreamingClient(
            Tool("task", """{"agent":"explore","label":"text","prompt":"inspect"}"""), Text("child"), Text("parent")), settings: settings);
        await session.Session.StartAsync(CancellationToken.None);
        await session.RunTurnAsync("delegate");
        Assert.DoesNotContain("view_image", Assert.Single(session.ReadSaved().Children).Tools);
    }

    [Fact]
    public async Task ChildView_UsesUniquePrefixAndNeverSubmitsToTheChild()
    {
        using var session = new HeadlessSession(new ScriptedStreamingClient(
            Tool("task", """{"agent":"explore","label":"first","prompt":"inspect"}"""), Text("child"), Text("parent"), Text("follow up")),
            settings: Settings(), chooseChild: (children, _) => Task.FromResult<string?>(children[0].Id));
        await session.Session.StartAsync(CancellationToken.None);
        await session.RunTurnAsync("delegate");
        var child = Assert.Single(session.ReadSaved().Children);
        await session.Session.SubmitAsync("/child", CancellationToken.None);
        Assert.Equal(child.Id, session.Observer.Events.OfType<ChildViewRequested>().Last().ChildSessionId);
        await session.Session.SubmitAsync("/child " + child.Id[..8], CancellationToken.None);
        Assert.Equal(child.Id, session.Observer.Events.OfType<ChildViewRequested>().Last().ChildSessionId);
        await session.RunTurnAsync("ordinary follow up");
        Assert.Equal(child.Archive.Length, Assert.Single(session.ReadSaved().Children).Archive.Length);
        await session.Session.SubmitAsync("/child parent", CancellationToken.None);
        Assert.Null(session.Observer.Events.OfType<ChildViewRequested>().Last().ChildSessionId);
    }
}
