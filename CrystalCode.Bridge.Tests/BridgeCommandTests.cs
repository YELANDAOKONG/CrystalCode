using System.Text.Json;

using Xunit;

using CrystalCode.Bridge.Tests.Support;
using CrystalCode.Bridge.Wire;
using CrystalCode.Engine.Events;

namespace CrystalCode.Bridge.Tests;

public sealed class BridgeCommandTests
{
    [Theory]
    [InlineData("/side explain", "extensions")]
    [InlineData("/btw explain", "extensions")]
    [InlineData("/child", "inspection")]
    [InlineData("/child parent", "inspection")]
    [InlineData("/resume", "/resume <id>")]
    [InlineData("/resume all", "/resume <id>")]
    [InlineData("/continue ALL", "/resume <id>")]
    [InlineData("/continue", "/resume <id>")]
    [InlineData("/edit", "terminal-only")]
    [InlineData("/editor", "terminal-only")]
    public async Task UnsupportedCommands_RefuseBeforeCallingTheModel(string command, string expected)
    {
        var model = new ScriptedModel(false);
        await using var fixture = await BridgeFixture.StartAsync(model);
        var id = await fixture.OpenAsync();
        var response = await PromptAsync(fixture, id, command);
        Assert.Contains(expected, response.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Empty(model.Requests);
    }

    [Fact]
    public async Task ChildPicker_WithExtensionsButNoFormsExplainsTheExplicitEntry()
    {
        await using var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false));
        var id = await fixture.OpenAsync(new { _meta = new { crystal = new { extensions = new { } } } });
        var response = await PromptAsync(fixture, id, "/child");
        Assert.Contains("/child <id>", response.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, "agent_message_chunk")]
    [InlineData(true, "notice")]
    public async Task CommandNotes_AreVisibleOnce(bool notices, string kind)
    {
        await using var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false));
        var id = await fixture.OpenAsync(notices ? new { session = new { notices = new { } } } : null);
        await PromptAsync(fixture, id, "/plan");
        var notes = fixture.Updates().Where(update => Event(update) == "NoteWritten").ToArray();
        Assert.Single(notes);
        Assert.Equal(kind, notes[0].GetProperty("sessionUpdate").GetString());
    }

    [Theory]
    [InlineData("/model", "ollama")]
    [InlineData("/sessions", "session")]
    public async Task QueryNotes_ArePlainAgentText(string command, string expected)
    {
        await using var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false));
        var id = await fixture.OpenAsync();
        await PromptAsync(fixture, id, command);
        var notes = fixture.Updates().Where(update => Event(update) == "NoteWritten").ToArray();
        Assert.NotEmpty(notes);
        Assert.All(notes, note => Assert.Equal("agent_message_chunk", note.GetProperty("sessionUpdate").GetString()));
        Assert.Contains(expected, string.Join("\n", notes.Select(note => note.GetProperty("content").GetProperty("text").GetString())), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BackgroundNotes_DoNotBecomeChatMessages()
    {
        var updates = new List<string>();
        var wire = new SessionUpdates(write =>
        {
            using var stream = new MemoryStream();
            using var writer = new Utf8JsonWriter(stream);
            write(writer);
            writer.Flush();
            updates.Add(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
        }, () => null, false);
        wire.Apply(new NoteWritten("Background note"));
        Assert.Empty(updates);
        wire.Apply(new NoteWritten("Command result") { Origin = "prompt-1" });
        Assert.Single(updates);
    }

    [Fact]
    public async Task Title_IsOfferedWithoutTopicOptionsAndRenamesTheSessionWithoutCallingTheModel()
    {
        var model = new ScriptedModel(false, ScriptedModel.TextRound("done"));
        await using var fixture = await BridgeFixture.StartAsync(model);
        var id = await fixture.OpenAsync();
        var offered = await fixture.WaitForUpdateAsync("available_commands_update");
        var commands = offered.GetProperty("params").GetProperty("update").GetProperty("availableCommands").EnumerateArray().ToArray();
        var title = Assert.Single(commands, command => command.GetProperty("name").GetString() == "title");
        Assert.False(title.TryGetProperty("input", out _));
        await PromptAsync(fixture, id, "First message");

        var response = await PromptAsync(fixture, id, "/title clear");
        Assert.Equal("end_turn", response.GetProperty("result").GetProperty("stopReason").GetString());
        await fixture.WaitForUpdateAsync("session_info_update", static update =>
            update.TryGetProperty("title", out var text) && text.GetString() == "clear");
        var listed = await fixture.RequestAsync("session/list", new { cwd = fixture.Workspace });
        var saved = Assert.Single(listed.GetProperty("result").GetProperty("sessions").EnumerateArray());
        Assert.Equal("clear", saved.GetProperty("title").GetString());
        Assert.Single(model.Requests);
    }

    [Theory]
    [InlineData("/title", "Use /title <text>.")]
    [InlineData("/title \"\"...", "Use /title <text>.")]
    public async Task InvalidTitle_ReportsTheEngineUsageWithoutCallingTheModel(string command, string expected)
    {
        var model = new ScriptedModel(false);
        await using var fixture = await BridgeFixture.StartAsync(model);
        var id = await fixture.OpenAsync();

        var response = await PromptAsync(fixture, id, command);

        Assert.Equal("command_refused", response.GetProperty("error").GetProperty("data").GetProperty("crystal").GetProperty("kind").GetString());
        Assert.Contains(expected, response.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Empty(model.Requests);
    }

    [Fact]
    public async Task Title_WhileBusyIsRefusedWithoutCancellingThePrompt()
    {
        var model = new ScriptedModel(true);
        await using var fixture = await BridgeFixture.StartAsync(model);
        var id = await fixture.OpenAsync();
        await PromptAsync(fixture, id, "/title Original");
        var held = model.Held.Task;
        var (_, running) = await fixture.StartRequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "Keep working" } } });
        await held.WaitAsync(BridgeFixture.Wait);

        var refused = await PromptAsync(fixture, id, "/title Changed");
        Assert.Equal("command_refused", refused.GetProperty("error").GetProperty("data").GetProperty("crystal").GetProperty("kind").GetString());
        Assert.Contains("Finish the current turn before using /title.", refused.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.False(running.IsCompleted);
        Assert.Single(model.Requests);
        await fixture.NotifyAsync("session/cancel", new { sessionId = id });
        await running.WaitAsync(BridgeFixture.Wait);
    }

    [Fact]
    public async Task PlainClient_CompletionsHideUnsupportedViewsAndExplainResume()
    {
        await using var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false));
        await fixture.OpenAsync();
        var message = await fixture.WaitForUpdateAsync("available_commands_update");
        var commands = message.GetProperty("params").GetProperty("update").GetProperty("availableCommands").EnumerateArray().ToArray();
        Assert.DoesNotContain(commands, command => command.GetProperty("name").GetString() is "btw" or "side" or "child" or "edit" or "editor");
        Assert.Contains("explicit session id", commands.Single(command => command.GetProperty("name").GetString() == "resume").GetProperty("description").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/quick send sample")]
    [InlineData("/shortcuts send sample")]
    public async Task QuickSend_WaitsWithoutDuplicatingTheClientMessage(string command)
    {
        var model = new ScriptedModel(true);
        await using var fixture = await BridgeFixture.StartAsync(model);
        Directory.CreateDirectory(Path.Combine(fixture.Home, "shortcuts"));
        File.WriteAllText(Path.Combine(fixture.Home, "shortcuts", "sample.md"), "Expanded template\n");
        var id = await fixture.OpenAsync();
        var held = model.Held.Task;
        var (_, response) = await fixture.StartRequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = command } } });
        await held.WaitAsync(BridgeFixture.Wait);
        Assert.False(response.IsCompleted);
        Assert.DoesNotContain(fixture.Updates(), update => update.GetProperty("sessionUpdate").GetString() == "user_message_chunk");
        Assert.Equal("Expanded template\n", model.LastUserText());
        await fixture.NotifyAsync("session/cancel", new { sessionId = id });
        Assert.Equal("cancelled", (await response.WaitAsync(BridgeFixture.Wait)).GetProperty("result").GetProperty("stopReason").GetString());
    }

    [Fact]
    public async Task QuickSend_HistoryReplaysTheExpandedTemplate()
    {
        await using var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false));
        Directory.CreateDirectory(Path.Combine(fixture.Home, "shortcuts"));
        File.WriteAllText(Path.Combine(fixture.Home, "shortcuts", "sample.md"), "Expanded history template");
        var id = await fixture.OpenAsync();
        await PromptAsync(fixture, id, "/quick send sample");
        await fixture.RequestAsync("session/close", new { sessionId = id });
        await fixture.RequestAsync("session/load", new { sessionId = id, cwd = fixture.Workspace });
        var user = Assert.Single(fixture.Updates(), update => update.GetProperty("sessionUpdate").GetString() == "user_message_chunk");
        Assert.Equal("Expanded history template", user.GetProperty("content").GetProperty("text").GetString());
        Assert.Equal("HistoryReplayed", Event(user));
    }

    [Fact]
    public async Task QuickSend_InvalidNameWhileBusyUsesTheEngineRefusal()
    {
        var model = new ScriptedModel(true);
        await using var fixture = await BridgeFixture.StartAsync(model);
        var id = await fixture.OpenAsync();
        var held = model.Held.Task;
        var (_, first) = await fixture.StartRequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "long" } } });
        await held.WaitAsync(BridgeFixture.Wait);
        var response = await PromptAsync(fixture, id, "/quick send missing");
        Assert.Equal("command_refused", response.GetProperty("error").GetProperty("data").GetProperty("crystal").GetProperty("kind").GetString());
        Assert.Contains(fixture.Updates(), update => Event(update) == "ErrorWritten");
        Assert.False(first.IsCompleted);
        await fixture.NotifyAsync("session/cancel", new { sessionId = id });
        await first.WaitAsync(BridgeFixture.Wait);
    }

    [Fact]
    public async Task QuickSend_WhileBusyWaitsWithoutInterruptingAndCancelsTheQueue()
    {
        var model = new ScriptedModel(true);
        await using var fixture = await BridgeFixture.StartAsync(model);
        Directory.CreateDirectory(Path.Combine(fixture.Home, "shortcuts"));
        File.WriteAllText(Path.Combine(fixture.Home, "shortcuts", "sample.md"), "Queued template");
        var id = await fixture.OpenAsync(new { _meta = new { crystal = new { extensions = new { } } } });
        var held = model.Held.Task;
        var (_, first) = await fixture.StartRequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "long" } } });
        await held.WaitAsync(BridgeFixture.Wait);
        var (_, second) = await fixture.StartRequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "/quick send sample" } } });
        await fixture.WaitForAsync(message => message.TryGetProperty("method", out var method) && method.GetString() == "_crystal/queue"
            && message.GetProperty("params").GetProperty("items").GetArrayLength() > 0);
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        Assert.Single(model.Requests);
        await fixture.NotifyAsync("session/cancel", new { sessionId = id });
        Assert.Equal("cancelled", (await first.WaitAsync(BridgeFixture.Wait)).GetProperty("result").GetProperty("stopReason").GetString());
        Assert.Equal("cancelled", (await second.WaitAsync(BridgeFixture.Wait)).GetProperty("result").GetProperty("stopReason").GetString());
        var snapshot = await fixture.RequestAsync("_crystal/snapshot", new { sessionId = id });
        Assert.True(snapshot.TryGetProperty("result", out _));
    }

    [Fact]
    public async Task QuickSend_AfterFailedTurnCompletesItsOwnTurn()
    {
        var fail = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var model = new ScriptedModel(true) { FailHeld = fail };
        await using var fixture = await BridgeFixture.StartAsync(model);
        Directory.CreateDirectory(Path.Combine(fixture.Home, "shortcuts"));
        File.WriteAllText(Path.Combine(fixture.Home, "shortcuts", "sample.md"), "Queued template");
        var id = await fixture.OpenAsync(new { _meta = new { crystal = new { extensions = new { } } } });
        var held = model.Held.Task;
        var (_, first) = await fixture.StartRequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "long" } } });
        await held.WaitAsync(BridgeFixture.Wait);
        var (_, second) = await fixture.StartRequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "/quick send sample" } } });
        await fixture.WaitForAsync(message => message.TryGetProperty("method", out var method) && method.GetString() == "_crystal/queue"
            && message.GetProperty("params").GetProperty("items").GetArrayLength() > 0);
        fail.SetResult();
        Assert.True((await first.WaitAsync(BridgeFixture.Wait)).TryGetProperty("error", out _));
        var response = await second.WaitAsync(BridgeFixture.Wait);
        Assert.Equal("end_turn", response.GetProperty("result").GetProperty("stopReason").GetString());
        Assert.Equal("Queued template", model.LastUserText());
        Assert.DoesNotContain(fixture.Updates(), update => Event(update) == "UserMessageSent");
    }

    [Theory]
    [InlineData("/quick")]
    [InlineData("/quick list")]
    [InlineData("/quick send missing")]
    [InlineData("/quick fill sample")]
    public async Task QuickQueriesAndInvalidInput_ReturnWithoutWaitingForATurn(string command)
    {
        var model = new ScriptedModel(false);
        await using var fixture = await BridgeFixture.StartAsync(model);
        var id = await fixture.OpenAsync();
        var response = await PromptAsync(fixture, id, command);
        if (command is "/quick" or "/quick list")
        {
            Assert.Equal("end_turn", response.GetProperty("result").GetProperty("stopReason").GetString());
            var report = Assert.Single(fixture.Updates(), update => Event(update) == "DocumentWritten");
            Assert.Contains("No quick prompts found.", report.GetProperty("content").GetProperty("text").GetString(), StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal("command_refused", response.GetProperty("error").GetProperty("data").GetProperty("crystal").GetProperty("kind").GetString());
        }

        Assert.Empty(model.Requests);
    }

    [Theory]
    [InlineData(".")]
    [InlineData("absolute")]
    [InlineData("directory")]
    public async Task ResumeDirectoryWithoutForms_RefusesPickerAndKeepsTheSession(string target)
    {
        var model = new ScriptedModel(false);
        await using var fixture = await BridgeFixture.StartAsync(model);
        var id = await fixture.OpenAsync();
        await PromptAsync(fixture, id, "save this session");
        var argument = target switch
        {
            "absolute" => fixture.Workspace,
            "directory" => Directory.CreateDirectory(Path.Combine(fixture.Workspace, "existing")).Name,
            _ => target
        };
        var response = await PromptAsync(fixture, id, "/resume " + argument);
        Assert.Contains("/resume <id>", response.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
        var after = await PromptAsync(fixture, id, "still active");
        Assert.True(after.TryGetProperty("result", out _));
    }

    [Fact]
    public async Task PluginOverrides_WithNoClientCapabilitiesRemainAvailable()
    {
        await using var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false),
            options: options => options with { Plugins = "on" }, prepare: PrepareCommandPlugin);
        var id = await fixture.OpenAsync();
        var menu = await fixture.WaitForUpdateAsync("available_commands_update");
        var commands = menu.GetProperty("params").GetProperty("update").GetProperty("availableCommands").EnumerateArray().ToArray();
        foreach (var name in new[] { "edit", "btw", "child", "resume", "side", "editor", "continue" })
        {
            Assert.Contains(commands, command => command.GetProperty("name").GetString() == name
                && command.GetProperty("description").GetString() == "Plugin " + name);
            var response = await PromptAsync(fixture, id, "/" + name);
            Assert.True(response.TryGetProperty("result", out _), response.ToString());
        }

        Assert.Contains(fixture.Updates(), update => Event(update) == "NoteWritten"
            && update.GetProperty("content").GetProperty("text").GetString()!.Contains("Plugin handled edit", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PluginOverrides_KeepTheirCompletionsAndDoNotWaitOnAnUnrelatedQueue()
    {
        var model = new ScriptedModel(true);
        await using var fixture = await BridgeFixture.StartAsync(model,
            options: options => options with { Plugins = "on" },
            prepare: PrepareCommandPlugin);
        var id = await fixture.OpenAsync(new { _meta = new { crystal = new { extensions = new { } } } });
        var menu = await fixture.WaitForUpdateAsync("available_commands_update");
        var commands = menu.GetProperty("params").GetProperty("update").GetProperty("availableCommands").EnumerateArray().ToArray();
        foreach (var name in new[] { "edit", "btw", "child", "resume", "side", "editor", "continue" })
        {
            Assert.Contains(commands, command => command.GetProperty("name").GetString() == name
                && command.GetProperty("description").GetString() == "Plugin " + name);
            var response = await PromptAsync(fixture, id, "/" + name);
            Assert.True(response.TryGetProperty("result", out _), response.ToString());
        }

        var held = model.Held.Task;
        var (_, first) = await fixture.StartRequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "long" } } });
        await held.WaitAsync(BridgeFixture.Wait);
        var (_, queued) = await fixture.StartRequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = "follow up" } } });
        await fixture.WaitForAsync(message => message.TryGetProperty("method", out var method) && method.GetString() == "_crystal/queue"
            && message.GetProperty("params").GetProperty("items").GetArrayLength() > 0);
        foreach (var name in new[] { "quick", "shortcuts" })
        {
            var response = await PromptAsync(fixture, id, "/" + name + " send missing");
            Assert.True(response.TryGetProperty("result", out _), response.ToString());
        }

        Assert.False(first.IsCompleted);
        Assert.False(queued.IsCompleted);
        await fixture.NotifyAsync("session/cancel", new { sessionId = id });
        await first.WaitAsync(BridgeFixture.Wait);
        await queued.WaitAsync(BridgeFixture.Wait);
    }

    private static void PrepareCommandPlugin(CrystalCode.Engine.Home.CrystalHome home, string workspace)
    {
        var directory = Directory.CreateDirectory(Path.Combine(home.PluginsDirectory, "commands")).FullName;
        File.Copy(typeof(CommandPlugin).Assembly.Location, Path.Combine(directory, "Commands.dll"));
        File.WriteAllText(Path.Combine(directory, "plugin.json"),
            """{"enabled":true,"assembly":"Commands.dll","type":"CrystalCode.Bridge.Tests.Support.CommandPlugin"}""");
    }

    private static string? Event(JsonElement update) => update.TryGetProperty("_meta", out var meta)
        && meta.GetProperty("crystal").TryGetProperty("event", out var value) ? value.GetString() : null;

    private static Task<JsonElement> PromptAsync(BridgeFixture fixture, string id, string text) =>
        fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text } } });
}
