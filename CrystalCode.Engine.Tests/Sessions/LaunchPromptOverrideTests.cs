using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class LaunchPromptOverrideTests
{
    [Fact]
    public async Task PromptSetSwitch_IsRefusedWhileTheLaunchFlagHolds()
    {
        using var headless = new HeadlessSession(
            new ScriptedStreamingClient(),
            settings: HeadlessSession.ScriptedSettings().WithPromptSetOverride("concise"),
            prepare: PrepareSets);
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.Session.SubmitAsync("/promptset other", CancellationToken.None);

        Assert.Equal(
            "Prompt set concise is forced for this process.",
            LastError(headless));
        Assert.Contains("\"enabled\": false", ReadManifest(headless, "promptsets", "other"), StringComparison.Ordinal);
        Assert.Contains("\"enabled\": false", ReadManifest(headless, "promptsets", "concise"), StringComparison.Ordinal);

        await headless.Session.SubmitAsync("/promptset", CancellationToken.None);
        Assert.Contains("Prompt Set: concise", LastNote(headless), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PromptAttachmentChange_IsRefusedWhenTheLaunchSwitchIsOff()
    {
        using var headless = new HeadlessSession(
            new ScriptedStreamingClient(),
            settings: HeadlessSession.ScriptedSettings().WithUsePromptAttachments(false),
            prepare: PrepareAttachment);
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.Session.SubmitAsync("/promptattach enable alpha", CancellationToken.None);

        Assert.Equal("Prompt attachments are off for this process.", LastError(headless));
        Assert.Contains(
            "\"enabled\": false",
            ReadManifest(headless, "prompt-attachments", "alpha"),
            StringComparison.Ordinal);

        await headless.Session.SubmitAsync("/promptattach", CancellationToken.None);
        Assert.Contains("alpha", LastNote(headless), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PromptAttachmentChange_StillAppliesUnderAForcedPromptSet()
    {
        using var headless = new HeadlessSession(
            new ScriptedStreamingClient(),
            settings: HeadlessSession.ScriptedSettings().WithPromptSetOverride("concise"),
            prepare: (home, workspace) =>
            {
                PrepareSets(home, workspace);
                PrepareAttachment(home, workspace);
            });
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.Session.SubmitAsync("/promptattach enable alpha", CancellationToken.None);

        Assert.Contains(
            "\"enabled\": true",
            ReadManifest(headless, "prompt-attachments", "alpha"),
            StringComparison.Ordinal);
        await headless.Session.SubmitAsync("/promptset", CancellationToken.None);
        Assert.Contains("Prompt Set: concise", LastNote(headless), StringComparison.Ordinal);
    }

    private static void PrepareSets(CrystalHome home, string workspace)
    {
        _ = workspace;
        WriteSet(Path.Combine(home.PromptSetsDirectory, "concise"), "concise work");
        WriteSet(Path.Combine(home.PromptSetsDirectory, "other"), "other work");
    }

    private static void PrepareAttachment(CrystalHome home, string workspace)
    {
        _ = workspace;
        var directory = Path.Combine(home.PromptAttachmentsDirectory, "alpha");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "work.md"), "ALPHA");
        File.WriteAllText(
            Path.Combine(directory, "prompt.json"),
            "{\n  \"enabled\": false\n}\n");
    }

    private static void WriteSet(string directory, string work)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "work.md"), work);
        File.WriteAllText(
            Path.Combine(directory, "prompt.json"),
            "{\n  \"enabled\": false\n}\n");
    }

    private static string ReadManifest(HeadlessSession headless, string folder, string name) =>
        File.ReadAllText(Path.Combine(headless.Home.Root, folder, name, "prompt.json"));

    private static string LastNote(HeadlessSession headless) =>
        headless.Observer.Events.OfType<NoteWritten>().Last().Text;

    private static string LastError(HeadlessSession headless) =>
        headless.Observer.Events.OfType<ErrorWritten>().Last().Text;
}
