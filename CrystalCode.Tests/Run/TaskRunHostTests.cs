using System.Text.Json;

using Crystal;
using Crystal.Chat;
using Crystal.Reasoning;

using CrystalCode.Commands;
using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Plugins.Interfaces;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Tools;
using CrystalCode.Run;
using CrystalCode.Terminal;

using Xunit;

namespace CrystalCode.Tests.Run;

public sealed class TaskRunHostTests
{
    private const string HiddenThinking = "trace-not-for-ci";

    private const string AllowReview =
        """
        {
          "outcome": "allow",
          "risk_level": "low",
          "user_authorization": "high",
          "rationale": "The task asked for this command."
        }
        """;

    [Theory]
    [InlineData("read", "read", "full", true)]
    [InlineData("write", "write", "full", true)]
    [InlineData("read", "write", "full", true)]
    [InlineData("read", "write", "edit", false)]
    public async Task DirectoryAccess_SeedsSessionWithoutChangingPreferences(
        string access, string tool, string approval, bool writesAllowed)
    {
        using var fixture = new RunFixture();
        var directory = Directory.CreateDirectory(Path.Combine(fixture.Home, "reference")).FullName;
        var file = Path.Combine(directory, "note.txt");
        await File.WriteAllTextAsync(file, "original");
        var settings = new TaskRunSettings
        {
            TaskText = "Use the reference", Home = fixture.Home, Workspace = fixture.Workspace,
            Provider = "ollama", Model = "qwen3:8b", Approval = approval,
            Skills = "off", ExternalTools = "off", Duration = "60",
            AllowRead = access == "read" ? [directory] : [],
            AllowWrite = access == "write" ? [directory] : []
        };
        var client = new ScriptedRunClient(AllowReview,
            ToolRound("c1", tool, JsonSerializer.Serialize(new { path = file, contents = "changed" })),
            TextRound("done"));

        var result = await fixture.RunAsync(client, settings);

        Assert.Equal(RunExit.Completed, result.Code);
        Assert.Equal(tool == "write" && writesAllowed ? "changed" : "original", await File.ReadAllTextAsync(file));
        if (!writesAllowed)
        {
            Assert.Contains("read-only", result.Output, StringComparison.Ordinal);
        }

        var store = new SessionStore(new CrystalHome(fixture.Home));
        Assert.True(store.TryLoadLatest(fixture.Workspace, out var saved));
        Assert.Equal(access, Assert.Single(saved.Access).Mode);
        fixture.AssertSettingsUnchanged();
    }

    [Fact]
    public async Task Completed_PrintsTheReplyAndKeepsSavedSettings()
    {
        using var fixture = new RunFixture();
        var client = new ScriptedRunClient(
            AllowReview,
            TextRound("Hello there.", HiddenThinking));

        var result = await fixture.RunAsync(client, "Say hello");

        Assert.Equal(RunExit.Completed, result.Code);
        Assert.Contains("Hello there.", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(HiddenThinking, result.Output, StringComparison.Ordinal);
        Assert.Contains("Resume with crystal --resume ", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Stopped", result.Output, StringComparison.Ordinal);
        Assert.Equal(string.Empty, result.Error);
        fixture.AssertSettingsUnchanged();
    }

    [Fact]
    public async Task UntrustedWorkspace_IsDeniedWithoutCreatingASession()
    {
        using var fixture = new RunFixture();
        var untrusted = Directory.CreateTempSubdirectory("crystal-run-untrusted-").FullName;
        try
        {
            var result = await fixture.RunAsync(
                new ScriptedRunClient(AllowReview, TextRound("should not run")),
                WorkspaceSettings(fixture, untrusted, workspaceTrust: "on"));

            Assert.Equal(RunExit.Denied, result.Code);
            Assert.Contains("not trusted", result.Error, StringComparison.Ordinal);
            Assert.Contains("--workspace-trust off", result.Error, StringComparison.Ordinal);
            Assert.Equal(string.Empty, result.Output);
            Assert.False(new WorkspaceTrustStore(new CrystalHome(fixture.Home)).Contains(untrusted));
            fixture.AssertSettingsUnchanged();
        }
        finally
        {
            Directory.Delete(untrusted, recursive: true);
        }
    }

    [Fact]
    public async Task UntrustedWorkspace_StaysDeniedWhenInteractiveTrustIsOff()
    {
        using var fixture = new RunFixture();
        var untrusted = Directory.CreateTempSubdirectory("crystal-run-untrusted-").FullName;
        try
        {
            File.WriteAllText(
                Path.Combine(fixture.Home, "config.json"),
                """
                {
                  "provider": "ollama",
                  "model": "qwen3:8b",
                  "workspaceTrust": false
                }
                """);

            var result = await fixture.RunAsync(
                new ScriptedRunClient(AllowReview, TextRound("should not run")),
                WorkspaceSettings(fixture, untrusted, workspaceTrust: null));

            Assert.Equal(RunExit.Denied, result.Code);
            Assert.Contains("not trusted", result.Error, StringComparison.Ordinal);
            Assert.Contains(
                "\"workspaceTrust\": false",
                File.ReadAllText(Path.Combine(fixture.Home, "config.json")),
                StringComparison.Ordinal);
            Assert.False(new WorkspaceTrustStore(new CrystalHome(fixture.Home)).Contains(untrusted));
        }
        finally
        {
            Directory.Delete(untrusted, recursive: true);
        }
    }

    [Fact]
    public async Task OperatorSpace_RunsWithoutRecordingTrust()
    {
        using var fixture = new RunFixture();
        Directory.CreateDirectory(Path.Combine(fixture.Home, ".git"));
        var space = OperatorSpace.EnsureCreated(new CrystalHome(fixture.Home));
        var before = File.ReadAllText(Path.Combine(fixture.Home, "trusted.json"));

        var result = await fixture.RunAsync(
            new ScriptedRunClient(AllowReview, TextRound("Hello there.")),
            WorkspaceSettings(fixture, space, workspaceTrust: "on"));

        Assert.Equal(RunExit.Completed, result.Code);
        Assert.Contains("Hello there.", result.Output, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(Path.Combine(fixture.Home, "trusted.json")));
        Assert.DoesNotContain(space, before, StringComparison.Ordinal);
        fixture.AssertSettingsUnchanged();
    }

    [Fact]
    public async Task DisableTool_OmitsTheBuiltInAndDoesNotSave()
    {
        using var fixture = new RunFixture();
        var client = new ScriptedRunClient(AllowReview, TextRound("Hello there."));
        var settings = fixture.Settings("Say hello");
        settings = new TaskRunSettings
        {
            TaskText = settings.TaskText,
            Home = settings.Home,
            Workspace = settings.Workspace,
            Provider = settings.Provider,
            Model = settings.Model,
            Skills = settings.Skills,
            ExternalTools = settings.ExternalTools,
            Duration = settings.Duration,
            DisableTool = ["read", "bash"]
        };

        var result = await fixture.RunAsync(client, settings);

        Assert.Equal(RunExit.Completed, result.Code);
        Assert.Contains("Disabled built-in tools  read, bash", result.Output, StringComparison.Ordinal);
        Assert.Contains("Do not call them.", client.SystemText, StringComparison.Ordinal);
        Assert.DoesNotContain(client.ToolNames, name => name == "read");
        Assert.DoesNotContain(client.ToolNames, name => name == "bash");
        Assert.Contains(client.ToolNames, name => name == "edit");
        fixture.AssertSettingsUnchanged();
    }

    [Fact]
    public async Task SpaceFlag_CreatesTheOperatorSpaceAndRunsThere()
    {
        using var fixture = new RunFixture();
        Directory.CreateDirectory(Path.Combine(fixture.Home, ".git"));
        var home = new CrystalHome(fixture.Home);
        var space = OperatorSpace.Resolve(home);
        var before = File.ReadAllText(Path.Combine(fixture.Home, "trusted.json"));
        var settings = fixture.Settings("Say hello");

        var result = await fixture.RunAsync(
            new ScriptedRunClient(AllowReview, TextRound("Hello there.")),
            new TaskRunSettings
            {
                TaskText = settings.TaskText,
                Home = settings.Home,
                Space = true,
                Provider = settings.Provider,
                Model = settings.Model,
                Skills = settings.Skills,
                ExternalTools = settings.ExternalTools,
                Duration = settings.Duration,
                WorkspaceTrust = "on"
            });

        Assert.Equal(RunExit.Completed, result.Code);
        Assert.Contains("Hello there.", result.Output, StringComparison.Ordinal);
        Assert.True(Directory.Exists(space));
        Assert.Equal(before, File.ReadAllText(Path.Combine(fixture.Home, "trusted.json")));
        Assert.NotEmpty(new SessionStore(home).List(space));
        fixture.AssertSettingsUnchanged();
    }

    [Fact]
    public async Task SpaceFlag_RejectsAWorkspacePath()
    {
        using var fixture = new RunFixture();
        var settings = fixture.Settings("Say hello");

        var result = await fixture.RunAsync(
            new ScriptedRunClient(AllowReview, TextRound("should not run")),
            new TaskRunSettings
            {
                TaskText = settings.TaskText,
                Home = settings.Home,
                Workspace = fixture.Workspace,
                Space = true,
                Provider = settings.Provider,
                Model = settings.Model,
                Skills = settings.Skills,
                ExternalTools = settings.ExternalTools,
                Duration = settings.Duration
            });

        Assert.Equal(RunExit.Invalid, result.Code);
        Assert.Contains("--workspace", result.Error, StringComparison.Ordinal);
        Assert.Equal(string.Empty, result.Output);
        Assert.False(Directory.Exists(OperatorSpace.Resolve(new CrystalHome(fixture.Home))));
    }

    [Fact]
    public async Task WorkspaceTrustOff_RunsWithoutRecordingTheDirectory()
    {
        using var fixture = new RunFixture();
        var untrusted = Directory.CreateTempSubdirectory("crystal-run-untrusted-").FullName;
        try
        {
            var result = await fixture.RunAsync(
                new ScriptedRunClient(AllowReview, TextRound("Hello there.")),
                WorkspaceSettings(fixture, untrusted, workspaceTrust: "off"));

            Assert.Equal(RunExit.Completed, result.Code);
            Assert.Contains("Hello there.", result.Output, StringComparison.Ordinal);
            Assert.False(new WorkspaceTrustStore(new CrystalHome(fixture.Home)).Contains(untrusted));
            fixture.AssertSettingsUnchanged();
        }
        finally
        {
            Directory.Delete(untrusted, recursive: true);
        }
    }

    [Fact]
    public async Task WorkspaceTrust_RejectsAValueOtherThanOnOrOff()
    {
        using var fixture = new RunFixture();
        var result = await fixture.RunAsync(
            new ScriptedRunClient(AllowReview, TextRound("should not run")),
            WorkspaceSettings(fixture, fixture.Workspace, workspaceTrust: "maybe"));

        Assert.Equal(RunExit.Invalid, result.Code);
        Assert.Contains("on or off", result.Error, StringComparison.Ordinal);
        Assert.Equal(string.Empty, result.Output);
    }

    [Fact]
    public async Task ShowThinking_PrintsReasoningWhenRequested()
    {
        using var fixture = new RunFixture();
        var client = new ScriptedRunClient(
            AllowReview,
            TextRound("Hello there.", HiddenThinking));
        var settings = fixture.Settings("Say hello");
        settings = new TaskRunSettings
        {
            TaskText = settings.TaskText,
            Home = settings.Home,
            Workspace = settings.Workspace,
            Provider = settings.Provider,
            Model = settings.Model,
            Skills = settings.Skills,
            ExternalTools = settings.ExternalTools,
            Duration = settings.Duration,
            ShowThinking = true
        };

        var result = await fixture.RunAsync(client, settings);

        Assert.Equal(RunExit.Completed, result.Code);
        Assert.Contains("[Thinking]", result.Output, StringComparison.Ordinal);
        Assert.Contains("[Assistant]", result.Output, StringComparison.Ordinal);
        Assert.Contains(HiddenThinking, result.Output, StringComparison.Ordinal);
        Assert.Contains("Hello there.", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReviewAllow_RunsTheShellCommand()
    {
        using var fixture = new RunFixture();
        var client = new ScriptedRunClient(
            AllowReview,
            ToolRound("c1", "bash", """{"command":"printf ready > ran.txt"}"""),
            TextRound("Wrote it."));
        var settings = fixture.Settings("Create the marker file");
        settings = Copy(settings, approval: "review");

        var result = await fixture.RunAsync(client, settings);

        Assert.Equal(RunExit.Completed, result.Code);
        Assert.Contains("printf ready > ran.txt", result.Output, StringComparison.Ordinal);
        Assert.Contains("Wrote it.", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("declined", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("ready", await File.ReadAllTextAsync(Path.Combine(fixture.Workspace, "ran.txt")));
        fixture.AssertSettingsUnchanged();
        Assert.Equal(ApprovalMode.Default, fixture.Reload().Approval);
    }

    [Fact]
    public async Task OperatorPrompt_DeniesTheShellAndExitsNonZero()
    {
        using var fixture = new RunFixture();
        var client = new ScriptedRunClient(
            AllowReview,
            ToolRound("c1", "bash", """{"command":"printf ready > ran.txt"}"""),
            TextRound("I could not run it."));

        var result = await fixture.RunAsync(client, "Create the marker file");

        Assert.Equal(RunExit.Denied, result.Code);
        Assert.Contains("The user declined this action.", result.Output, StringComparison.Ordinal);
        Assert.Contains("[Stopped] operator prompt denied", result.Output, StringComparison.Ordinal);
        Assert.Contains("I could not run it.", result.Output, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(fixture.Workspace, "ran.txt")));
        fixture.AssertSettingsUnchanged();
    }

    [Fact]
    public async Task ModelCallBudget_StopsBeforeTheNextRound()
    {
        using var fixture = new RunFixture();
        await File.WriteAllTextAsync(Path.Combine(fixture.Workspace, "notes.txt"), "alpha");
        var client = new ScriptedRunClient(
            AllowReview,
            ToolRound("c1", "read", """{"path":"notes.txt"}"""),
            TextRound("This round should not run."));
        var settings = Copy(fixture.Settings("Read the notes"), modelCalls: "1");

        var result = await fixture.RunAsync(client, settings);

        Assert.Equal(RunExit.Limited, result.Code);
        Assert.Contains("alpha", result.Output, StringComparison.Ordinal);
        Assert.Contains("[Stopped] model_call_limit_reached", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("This round should not run.", result.Output, StringComparison.Ordinal);
        Assert.Equal(
            TurnLimits.DefaultMaximumModelCalls,
            fixture.Reload().ExecutionBudget.MaximumModelCalls);
    }

    [Fact]
    public async Task PlanMode_DoesNotRunBash()
    {
        using var fixture = new RunFixture();
        var client = new ScriptedRunClient(
            AllowReview,
            ToolRound("c1", "bash", """{"command":"printf ready > ran.txt"}"""),
            TextRound("Plan cannot run commands."));
        var settings = Copy(fixture.Settings("Create the marker file"), plan: true);

        var result = await fixture.RunAsync(client, settings);

        // The model is told the tool is unavailable and gets another round,
        // instead of the whole run failing.
        Assert.Contains("Tool 'bash' is not available.", result.Output, StringComparison.Ordinal);
        Assert.Contains("Plan cannot run commands.", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("[Stopped] failed", result.Output, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(fixture.Workspace, "ran.txt")));
    }

    [Fact]
    public async Task Stdin_SuppliesTheTaskWhenNoArgumentIsPresent()
    {
        using var fixture = new RunFixture();
        var client = new ScriptedRunClient(AllowReview, TextRound("Ack"));
        var settings = fixture.Settings(task: null);

        var result = await fixture.RunAsync(
            client,
            settings,
            input: new StringReader("from stdin"),
            inputRedirected: true);

        Assert.Equal(RunExit.Completed, result.Code);
        Assert.Contains("Ack", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingTask_DoesNotReadATerminal()
    {
        using var fixture = new RunFixture();
        var client = new ScriptedRunClient(AllowReview, TextRound("should not run"));

        var result = await fixture.RunAsync(
            client,
            fixture.Settings(task: null),
            input: new StringReader("from stdin"),
            inputRedirected: false);

        Assert.Equal(RunExit.Invalid, result.Code);
        Assert.Contains("Pass a task", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("Session saved", result.Output, StringComparison.Ordinal);
        Assert.Equal(0, client.RequestCount);
    }

    [Fact]
    public async Task MissingPromptSet_ExitsInvalidWithoutChangingSettings()
    {
        using var fixture = new RunFixture();
        var client = new ScriptedRunClient(AllowReview, TextRound("unused"));
        var settings = Copy(fixture.Settings("hello"), promptSet: "missing-set");

        var result = await fixture.RunAsync(client, settings);

        Assert.Equal(RunExit.Invalid, result.Code);
        Assert.Contains("Prompt set not found  missing-set", result.Error, StringComparison.Ordinal);
        Assert.Equal(0, client.RequestCount);
        fixture.AssertSettingsUnchanged();
    }

    [Fact]
    public async Task JsonFormat_PrintsOneObjectPerLine()
    {
        using var fixture = new RunFixture();
        var client = new ScriptedRunClient(
            AllowReview,
            TextRound("Hello there.", HiddenThinking));
        var settings = Copy(fixture.Settings("Say hello"), format: "json");

        var result = await fixture.RunAsync(client, settings);

        Assert.Equal(RunExit.Completed, result.Code);
        Assert.Equal(string.Empty, result.Error);
        var events = ReadEvents(result.Output);
        Assert.Contains(events, static item => item.GetProperty("type").GetString() == "text"
            && item.GetProperty("text").GetString() == "Hello there.");
        Assert.Contains(events, static item => item.GetProperty("type").GetString() == "step_start");
        Assert.Contains(events, static item =>
            item.GetProperty("type").GetString() == "step_finish"
            && item.GetProperty("reason").GetString() == "completed");
        var session = Assert.Single(events, static item => item.GetProperty("type").GetString() == "session");
        var sessionId = session.GetProperty("sessionID").GetString();
        Assert.False(string.IsNullOrWhiteSpace(sessionId));
        Assert.Contains("crystal --resume " + sessionId, session.GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain(events, static item => item.GetProperty("type").GetString() == "reasoning");
        Assert.DoesNotContain(events, static item => item.GetProperty("type").GetString() == "stopped");
        Assert.All(events, item => Assert.Equal(sessionId, item.GetProperty("sessionID").GetString()));
        Assert.DoesNotContain(HiddenThinking, result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task JsonFormat_IncludesReasoningWhenRequested()
    {
        using var fixture = new RunFixture();
        var client = new ScriptedRunClient(AllowReview, TextRound("Hello there.", HiddenThinking));
        var settings = Copy(fixture.Settings("Say hello"), format: "json", showThinking: true);

        var result = await fixture.RunAsync(client, settings);

        Assert.Equal(RunExit.Completed, result.Code);
        var events = ReadEvents(result.Output);
        Assert.Contains(events, static item => item.GetProperty("type").GetString() == "reasoning"
            && item.GetProperty("text").GetString() == HiddenThinking);
    }

    [Fact]
    public async Task JsonFormat_ReportsACompletedTool()
    {
        using var fixture = new RunFixture();
        var client = new ScriptedRunClient(
            AllowReview,
            ToolRound("c1", "bash", """{"command":"printf ready > ran.txt"}"""),
            TextRound("Wrote it."));
        var settings = Copy(fixture.Settings("Create the marker file"), approval: "review", format: "json");

        var result = await fixture.RunAsync(client, settings);

        Assert.Equal(RunExit.Completed, result.Code);
        var events = ReadEvents(result.Output);
        var tool = Assert.Single(events, static item => item.GetProperty("type").GetString() == "tool_use");
        Assert.Equal("bash", tool.GetProperty("name").GetString());
        Assert.Equal("c1", tool.GetProperty("callId").GetString());
        Assert.Equal("success", tool.GetProperty("status").GetString());
        Assert.Equal("printf ready > ran.txt", tool.GetProperty("arguments").GetProperty("command").GetString());
        Assert.Contains("exit 0", tool.GetProperty("output").GetString(), StringComparison.Ordinal);
        Assert.Equal("ready", await File.ReadAllTextAsync(Path.Combine(fixture.Workspace, "ran.txt")));
    }

    [Fact]
    public async Task JsonFormat_ReportsADeniedTool()
    {
        using var fixture = new RunFixture();
        var client = new ScriptedRunClient(
            AllowReview,
            ToolRound("c1", "bash", """{"command":"printf ready > ran.txt"}"""),
            TextRound("I could not run it."));
        var settings = Copy(fixture.Settings("Create the marker file"), format: "json");

        var result = await fixture.RunAsync(client, settings);

        Assert.Equal(RunExit.Denied, result.Code);
        var events = ReadEvents(result.Output);
        var tool = Assert.Single(events, static item => item.GetProperty("type").GetString() == "tool_use");
        Assert.Equal("failure", tool.GetProperty("status").GetString());
        Assert.Contains("The user declined this action.", tool.GetProperty("output").GetString(), StringComparison.Ordinal);
        var stopped = Assert.Single(events, static item => item.GetProperty("type").GetString() == "stopped");
        Assert.Equal("operator prompt denied", stopped.GetProperty("status").GetString());
        Assert.False(File.Exists(Path.Combine(fixture.Workspace, "ran.txt")));
    }

    [Fact]
    public async Task JsonFormat_RejectsAnUnknownName()
    {
        using var fixture = new RunFixture();
        var client = new ScriptedRunClient(AllowReview, TextRound("unused"));
        var settings = Copy(fixture.Settings("hello"), format: "yaml");

        var result = await fixture.RunAsync(client, settings);

        Assert.Equal(RunExit.Invalid, result.Code);
        Assert.Contains("Format must be default or json.", result.Error, StringComparison.Ordinal);
        Assert.Equal(string.Empty, result.Output);
        Assert.Equal(0, client.RequestCount);
    }

    [Fact]
    public async Task SlashCommand_DoesNotChangeSettings()
    {
        using var fixture = new RunFixture();
        var client = new ScriptedRunClient(AllowReview, TextRound("unused"));

        var result = await fixture.RunAsync(client, "/approval full");

        Assert.Equal(RunExit.Invalid, result.Code);
        Assert.Contains("slash command", result.Error, StringComparison.Ordinal);
        Assert.Equal(ApprovalMode.Default, fixture.Reload().Approval);
        Assert.Equal(0, client.RequestCount);
    }

    [Fact]
    public void Overrides_ApplyOnlyInMemory()
    {
        var current = HarnessSettings.CreateDefault();
        var request = new TaskRunSettings
        {
            ModelCalls = "unlimited",
            ToolCalls = "0",
            Duration = "unlimited",
            BashTimeout = "unlimited",
            Skills = "off",
            ExternalTools = "off",
            Thinking = "max",
            Approval = "audit",
            PromptSet = "concise",
            PromptAttachments = "off"
        };

        var applied = AssertApplied(current, request);

        Assert.Null(applied.ExecutionBudget.MaximumModelCalls);
        Assert.Equal(0, applied.ExecutionBudget.MaximumToolCalls);
        Assert.Null(applied.ExecutionBudget.MaximumDuration);
        Assert.Null(applied.BashTimeoutSeconds);
        Assert.False(applied.Skills);
        Assert.False(applied.ExternalTools);
        Assert.Equal("max", applied.ThinkingEffort.Value);
        Assert.Equal(ApprovalMode.Audit, applied.Approval);
        Assert.Equal("concise", applied.PromptSetOverride);
        Assert.Equal(HarnessSettings.DefaultPromptSet, applied.PromptSet);
        Assert.False(applied.UsePromptAttachments);
        Assert.Null(current.PromptSetOverride);
        Assert.True(current.UsePromptAttachments);
        Assert.Equal(current.Provider, applied.Provider);
        Assert.Equal(TurnLimits.DefaultMaximumModelCalls, current.ExecutionBudget.MaximumModelCalls);
    }

    [Fact]
    public void RulesOverride_ChangesOnlyTheNamedSide()
    {
        var current = HarnessSettings.CreateDefault()
            .WithRuleFiles(RuleFileSettings.Default.WithGlobal(false));

        var applied = AssertApplied(current, new TaskRunSettings { Rules = "project=off" });

        Assert.False(applied.RuleFiles.Global);
        Assert.False(applied.RuleFiles.Project);
        Assert.False(current.RuleFiles.Global);
        Assert.True(current.RuleFiles.Project);
        Assert.False(TaskRunOverrides.TryApply(
            current,
            new TaskRunSettings { Rules = "project=false" },
            out _,
            out _,
            out var error));
        Assert.Contains("--rules", error, StringComparison.Ordinal);
    }

    [Fact]
    public void PromptAttachmentOverride_StaysInMemoryAndRejectsOff()
    {
        var current = HarnessSettings.CreateDefault();
        var applied = AssertApplied(
            current,
            new TaskRunSettings { PromptAttachment = [" beta ", "beta", "alpha"] });

        Assert.Equal(["beta", "alpha"], applied.PromptAttachments);
        Assert.Empty(current.PromptAttachments);
        Assert.True(applied.UsePromptAttachments);

        Assert.False(TaskRunOverrides.TryApply(
            current,
            new TaskRunSettings
            {
                PromptAttachments = "off",
                PromptAttachment = ["alpha"]
            },
            out _,
            out _,
            out var offError));
        Assert.Contains("--prompt-attachments is off", offError, StringComparison.Ordinal);

        Assert.False(TaskRunOverrides.TryApply(
            current,
            new TaskRunSettings { PromptAttachment = ["Bad_Name"] },
            out _,
            out _,
            out var invalidError));
        Assert.Contains("invalid", invalidError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PromptAttachment_MissingNameDoesNotStart()
    {
        using var fixture = new RunFixture();
        var client = new ScriptedRunClient(AllowReview, TextRound("unused"));
        var settings = Copy(fixture.Settings("Say hello"), promptAttachment: ["alpha"]);

        var result = await fixture.RunAsync(client, settings);

        Assert.Equal(RunExit.Invalid, result.Code);
        Assert.Contains("Prompt attachment not found  alpha", result.Error, StringComparison.Ordinal);
        Assert.Equal(0, client.RequestCount);
        fixture.AssertSettingsUnchanged();
    }

    [Fact]
    public async Task PromptAttachment_AppendsADisabledAttachmentWithoutWritingIt()
    {
        using var fixture = new RunFixture();
        var directory = Path.Combine(fixture.Home, "prompt-attachments", "alpha");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "work.md"), "ALPHA");
        File.WriteAllText(
            Path.Combine(directory, "prompt.json"),
            "{\n  \"enabled\": false\n}\n");
        var client = new ScriptedRunClient(AllowReview, TextRound("Done."));
        var settings = Copy(fixture.Settings("Say hello"), promptAttachment: ["alpha"]);

        var result = await fixture.RunAsync(client, settings);

        Assert.Equal(RunExit.Completed, result.Code);
        Assert.Contains("Prompt attachment 'alpha' is forced for this process.", result.Output, StringComparison.Ordinal);
        Assert.Contains("\"enabled\": false", File.ReadAllText(Path.Combine(directory, "prompt.json")), StringComparison.Ordinal);
        fixture.AssertSettingsUnchanged();
        Assert.DoesNotContain("promptAttachments", File.ReadAllText(Path.Combine(fixture.Home, "config.json")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApprovalModelOverride_StaysProcessOnly()
    {
        using var fixture = new RunFixture();
        var client = new ScriptedRunClient(AllowReview, TextRound("Hello there."));
        var settings = Copy(
            fixture.Settings("Say hello"),
            approvalModel: "on",
            approvalModelId: "qwen3:8b",
            approvalThinking: "high");

        var result = await fixture.RunAsync(client, settings);

        Assert.Equal(RunExit.Completed, result.Code);
        fixture.AssertSettingsUnchanged();
        Assert.False(fixture.Reload().ApprovalModel.Enabled);
        Assert.Equal(ThinkingSelection.Default, fixture.Reload().ApprovalModel.ThinkingEffort);
    }

    [Fact]
    public void Overrides_ConfiguresTheApprovalModelInMemory()
    {
        var stored = HarnessSettings.CreateDefault().WithApprovalModel(
            new ApprovalModelSettings(true, "openai", "gpt-5.6-sol"));

        var off = AssertApplied(stored, new TaskRunSettings { ApprovalModel = "off" });

        Assert.False(off.ApprovalModel.Enabled);
        Assert.Equal("openai", off.ApprovalModel.Provider);
        Assert.Equal("gpt-5.6-sol", off.ApprovalModel.Model);
        Assert.True(stored.ApprovalModel.Enabled);

        var on = AssertApplied(
            stored.WithApprovalModel(stored.ApprovalModel.DisabledCopy()),
            new TaskRunSettings { ApprovalModel = "on" });
        Assert.True(on.ApprovalModel.Enabled);
        Assert.Equal("gpt-5.6-sol", on.ApprovalModel.Model);

        var selected = AssertApplied(
            HarnessSettings.CreateDefault(),
            new TaskRunSettings
            {
                ApprovalProvider = "openai",
                ApprovalModelId = "gpt-5.6-terra"
            });
        Assert.True(selected.ApprovalModel.Enabled);
        Assert.Equal("openai", selected.ApprovalModel.Provider);
        Assert.Equal("gpt-5.6-terra", selected.ApprovalModel.Model);
        Assert.Equal(ProviderName.DeepSeek, selected.Provider);

        var geared = AssertApplied(
            HarnessSettings.CreateDefault(),
            new TaskRunSettings { ApprovalThinking = "high" });
        Assert.False(geared.ApprovalModel.Enabled);
        Assert.Equal("high", geared.ApprovalModel.ThinkingEffort.Value);

        var selectedWithGear = AssertApplied(
            stored,
            new TaskRunSettings
            {
                ApprovalModelId = "gpt-5.6-sol",
                ApprovalThinking = "high"
            });
        Assert.True(selectedWithGear.ApprovalModel.Enabled);
        Assert.Equal("gpt-5.6-sol", selectedWithGear.ApprovalModel.Model);
        Assert.Equal("high", selectedWithGear.ApprovalModel.ThinkingEffort.Value);
        Assert.Equal(ThinkingSelection.Default, stored.ApprovalModel.ThinkingEffort);

        var storedWithGear = stored.WithApprovalModel(
            stored.ApprovalModel.WithThinkingEffort(ThinkingSelection.Parse("high")));

        var onKeepsGear = AssertApplied(
            storedWithGear,
            new TaskRunSettings { ApprovalModel = "on" });
        Assert.True(onKeepsGear.ApprovalModel.Enabled);
        Assert.Equal("high", onKeepsGear.ApprovalModel.ThinkingEffort.Value);

        var selectedKeepsGear = AssertApplied(
            storedWithGear,
            new TaskRunSettings { ApprovalModelId = "gpt-5.6-sol" });
        Assert.True(selectedKeepsGear.ApprovalModel.Enabled);
        Assert.Equal("gpt-5.6-sol", selectedKeepsGear.ApprovalModel.Model);
        Assert.Equal("high", selectedKeepsGear.ApprovalModel.ThinkingEffort.Value);

        var offKeepsGear = AssertApplied(
            storedWithGear,
            new TaskRunSettings { ApprovalModel = "off" });
        Assert.False(offKeepsGear.ApprovalModel.Enabled);
        Assert.Equal("high", offKeepsGear.ApprovalModel.ThinkingEffort.Value);
    }

    [Fact]
    public void Overrides_ConfiguresTheCompactionModelInMemory()
    {
        var stored = HarnessSettings.CreateDefault().WithCompactionModel(
            new CompactionModelSettings(true, "openai", "gpt-5.6-sol"));

        var off = AssertApplied(stored, new TaskRunSettings { CompactionModel = "off" });

        Assert.False(off.CompactionModel.Enabled);
        Assert.Equal("openai", off.CompactionModel.Provider);
        Assert.Equal("gpt-5.6-sol", off.CompactionModel.Model);
        Assert.True(stored.CompactionModel.Enabled);

        var on = AssertApplied(
            stored.WithCompactionModel(stored.CompactionModel.DisabledCopy()),
            new TaskRunSettings { CompactionModel = "on" });
        Assert.True(on.CompactionModel.Enabled);
        Assert.Equal("gpt-5.6-sol", on.CompactionModel.Model);

        var selected = AssertApplied(
            HarnessSettings.CreateDefault(),
            new TaskRunSettings
            {
                CompactionProvider = "openai",
                CompactionModelId = "gpt-5.6-terra"
            });
        Assert.True(selected.CompactionModel.Enabled);
        Assert.Equal("openai", selected.CompactionModel.Provider);
        Assert.Equal("gpt-5.6-terra", selected.CompactionModel.Model);
        Assert.Equal(ProviderName.DeepSeek, selected.Provider);

        var geared = AssertApplied(
            HarnessSettings.CreateDefault(),
            new TaskRunSettings { CompactionThinking = "off" });
        Assert.False(geared.CompactionModel.Enabled);
        Assert.Equal(ThinkingSelection.Off, geared.CompactionModel.ThinkingEffort);

        Assert.False(TaskRunOverrides.TryApply(
            HarnessSettings.CreateDefault(),
            new TaskRunSettings { CompactionModel = "on" },
            out _,
            out _,
            out var missing));
        Assert.Equal("Pass --compaction-model-id.", missing);

        Assert.False(TaskRunOverrides.TryApply(
            stored,
            new TaskRunSettings { CompactionModel = "off", CompactionProvider = "openai" },
            out _,
            out _,
            out var combined));
        Assert.Equal(
            "Do not pass --compaction-provider or --compaction-model-id when --compaction-model is off.",
            combined);
    }

    [Fact]
    public void Overrides_RejectsApprovalModelConflicts()
    {
        var current = HarnessSettings.CreateDefault();

        Assert.False(TaskRunOverrides.TryApply(
            current,
            new TaskRunSettings { ApprovalModel = "off", ApprovalProvider = "openai" },
            out _,
            out _,
            out var combined));
        Assert.Equal(
            "Do not pass --approval-provider or --approval-model-id when --approval-model is off.",
            combined);

        Assert.False(TaskRunOverrides.TryApply(
            current,
            new TaskRunSettings { ApprovalModel = "on" },
            out _,
            out _,
            out var missing));
        Assert.Equal("Pass --approval-model-id.", missing);
        Assert.False(current.ApprovalModel.Enabled);

        Assert.False(TaskRunOverrides.TryApply(
            current,
            new TaskRunSettings { ApprovalThinking = "bad effort" },
            out _,
            out _,
            out var gearError));
        Assert.Contains("Thinking effort must be", gearError, StringComparison.Ordinal);
    }

    [Fact]
    public void Overrides_RejectsAnInvalidQuotaOrModePair()
    {
        var current = HarnessSettings.CreateDefault();

        Assert.False(TaskRunOverrides.TryApply(
            current,
            new TaskRunSettings { ModelCalls = "0" },
            out _,
            out _,
            out var quotaError));
        Assert.Contains("positive", quotaError, StringComparison.OrdinalIgnoreCase);

        Assert.False(TaskRunOverrides.TryApply(
            current,
            new TaskRunSettings { Plan = true, Work = true },
            out _,
            out _,
            out var modeError));
        Assert.Contains("--plan", modeError, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, false, "audit")]
    [InlineData(false, true, "audit")]
    [InlineData(true, true, null)]
    public void Overrides_RejectsMoreThanOneModeFlag(bool plan, bool work, string? mode)
    {
        Assert.False(TaskRunOverrides.TryApply(
            HarnessSettings.CreateDefault(),
            new TaskRunSettings { Plan = plan, Work = work, Mode = mode },
            out _,
            out _,
            out var error));
        Assert.Contains("--mode", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, false, null, "plan")]
    [InlineData(false, true, null, "work")]
    [InlineData(false, false, " audit ", "audit")]
    [InlineData(false, false, null, null)]
    public void Overrides_ReportsTheRequestedMode(bool plan, bool work, string? requested, string? expected)
    {
        Assert.True(TaskRunOverrides.TryApply(
            HarnessSettings.CreateDefault(),
            new TaskRunSettings { Plan = plan, Work = work, Mode = requested },
            out _,
            out var mode,
            out _));
        Assert.Equal(expected, mode);
    }

    [Fact]
    public void Overrides_AppliesDisabledBuiltInToolsWithoutSavingThem()
    {
        var current = HarnessSettings.CreateDefault();
        var applied = AssertApplied(
            current,
            new TaskRunSettings { DisableTool = [" read ", "bash", "read"] });

        Assert.Equal(["read", "bash"], applied.DisabledBuiltInTools.Plan);

        Assert.False(TaskRunOverrides.TryApply(
            current,
            new TaskRunSettings { DisableTool = ["deploy"] },
            out _,
            out _,
            out var error));
        Assert.Contains("Unknown built-in tool", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Overrides_RejectsAnUnknownProviderOrModel()
    {
        var current = HarnessSettings.CreateDefault();

        Assert.False(TaskRunOverrides.TryApply(
            current,
            new TaskRunSettings { Provider = "nosuchprovider" },
            out _,
            out _,
            out var providerError));
        Assert.Contains("nosuchprovider", providerError, StringComparison.Ordinal);
        Assert.Contains("not configured", providerError, StringComparison.Ordinal);

        Assert.False(TaskRunOverrides.TryApply(
            current,
            new TaskRunSettings { Model = "nosuch-model" },
            out _,
            out _,
            out var modelError));
        Assert.Contains("nosuch-model", modelError, StringComparison.Ordinal);
        Assert.Equal(ProviderName.DeepSeek, current.Provider);
        Assert.Equal("deepseek-flash", current.Model);
    }

    [Fact]
    public void Overrides_SelectsADeclaredContextBudgetInMemory()
    {
        var current = TieredContextSettings();
        var applied = AssertApplied(current, new TaskRunSettings { Context = "1m" });
        Assert.Equal(1_000_000, applied.ContextWindow);
        Assert.Equal(272_000, current.ContextWindow);
        Assert.Empty(current.ContextSelections);

        var exact = AssertApplied(current, new TaskRunSettings { Context = "272000" });
        Assert.Equal(272_000, exact.ContextWindow);

        var other = applied.WithSelection(applied.Provider, "small");
        Assert.Equal(128_000, other.ContextWindow);
        Assert.Equal(1_000_000, other.WithSelection(applied.Provider, "large").ContextWindow);

        Assert.False(TaskRunOverrides.TryApply(
            current.WithSelection(current.Provider, "small"),
            new TaskRunSettings { Context = "128000" },
            out _,
            out _,
            out var single));
        Assert.Equal("The selected model has one context budget.", single);

        Assert.False(TaskRunOverrides.TryApply(
            current,
            new TaskRunSettings { Context = "128k" },
            out _,
            out _,
            out var unavailable));
        Assert.Contains("not available", unavailable, StringComparison.Ordinal);
        Assert.Contains("272K", unavailable, StringComparison.Ordinal);

        Assert.False(TaskRunOverrides.TryApply(
            current,
            new TaskRunSettings { Context = " " },
            out _,
            out _,
            out var blank));
        Assert.Contains("Context budget is required.", blank, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnavailableThinking_PrintsANoteAndContinues()
    {
        using var fixture = new RunFixture();
        var client = new ScriptedRunClient(AllowReview, TextRound("Hello there."));
        var settings = fixture.Settings("Say hello");
        settings = new TaskRunSettings
        {
            TaskText = settings.TaskText,
            Home = settings.Home,
            Workspace = settings.Workspace,
            Provider = settings.Provider,
            Model = settings.Model,
            Skills = settings.Skills,
            ExternalTools = settings.ExternalTools,
            Duration = settings.Duration,
            Thinking = "bogus"
        };

        var result = await fixture.RunAsync(client, settings);

        Assert.Equal(RunExit.Completed, result.Code);
        Assert.Contains("Thinking effort 'bogus' is not available", result.Error, StringComparison.Ordinal);
        Assert.Contains("provider default", result.Error, StringComparison.Ordinal);
        Assert.Contains("Hello there.", result.Output, StringComparison.Ordinal);
        fixture.AssertSettingsUnchanged();
    }

    [Fact]
    public void UnavailableThinking_SkipsOffWhenTheModelDoesNotThink()
    {
        var settings = HarnessSettings.CreateDefault()
            .WithOverrides("openai", "gpt-5.6-sol")
            .WithThinkingEffort(ThinkingSelection.Off);

        Assert.Empty(TaskRunOverrides.UnavailableThinkingNotes(settings));

        var named = settings.WithThinkingEffort(ThinkingSelection.Parse("high"));
        Assert.Contains(
            "Thinking effort 'high' is not available",
            Assert.Single(TaskRunOverrides.UnavailableThinkingNotes(named)),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownProvider_ExitsInvalid()
    {
        using var fixture = new RunFixture();
        var client = new ScriptedRunClient(AllowReview, TextRound("unused"));
        var settings = fixture.Settings("hello");
        settings = new TaskRunSettings
        {
            TaskText = settings.TaskText,
            Home = settings.Home,
            Workspace = settings.Workspace,
            Provider = "nosuchprovider",
            Model = settings.Model
        };

        var result = await fixture.RunAsync(client, settings);

        Assert.Equal(RunExit.Invalid, result.Code);
        Assert.Contains("nosuchprovider", result.Error, StringComparison.Ordinal);
        Assert.Equal(0, client.RequestCount);
        fixture.AssertSettingsUnchanged();
    }

    [Fact]
    public void LaunchPlan_WorkLeavesAResumedPlanSession()
    {
        using var fixture = new RunFixture();
        var home = new CrystalHome(fixture.Home);
        var loaded = new DiskSettingsStore(home).Load();
        var plugins = new PluginRegistry();
        plugins.Add(new WorkspaceToolsPlugin());
        plugins.Add(new ScriptedRunPlugin(new ScriptedRunClient(AllowReview)));
        var session = CodingSession.Create(
            loaded.WithOverrides("ollama", "qwen3:8b"),
            new DiskSettingsStore(home),
            new CredentialStore(home),
            home,
            fixture.Workspace,
            new SessionFrontEnd(
                new RunLog(TextWriter.Null, showThinking: false),
                new UnattendedApprovalPrompt(),
                new UnattendedUserPrompt(),
                new UnattendedSessionChooser(),
                new UnattendedTrustPrompt()),
            plugins,
            new SessionDocument
            {
                Id = SessionStore.NewId(),
                Workspace = fixture.Workspace,
                PlanMode = true,
                Items =
                [
                    new SessionItemDocument { Kind = "message", Role = "user", Text = "hello" }
                ]
            });

        Assert.True(session.PlanMode);
        TerminalHost.ApplyLaunchMode(session, null);
        Assert.True(session.PlanMode);
        TerminalHost.ApplyLaunchMode(session, "work");
        Assert.False(session.PlanMode);
        TerminalHost.ApplyLaunchMode(session, "plan");
        Assert.True(session.PlanMode);
        Assert.Throws<ArgumentException>(() => TerminalHost.ApplyLaunchMode(session, "missing"));
    }

    [Fact]
    public void Exit_FailureAndBudgetOutrankDenial()
    {
        var failed = RunExit.From(TurnStopReason.Failed, operatorDenied: true);
        var limited = RunExit.From(TurnStopReason.ModelCallLimitReached, operatorDenied: true);
        var denied = RunExit.From(TurnStopReason.Completed, operatorDenied: true);
        var interrupted = RunExit.From(TurnStopReason.Interrupted, operatorDenied: true);

        var filtered = RunExit.From(TurnStopReason.ContentFiltered, operatorDenied: true);
        var truncated = RunExit.From(TurnStopReason.OutputTruncated, operatorDenied: true);

        Assert.Equal(RunExit.Failed, failed.Code);
        Assert.Equal(RunExit.Failed, filtered.Code);
        Assert.Equal("content_filtered", filtered.Status);
        Assert.Equal(RunExit.Limited, limited.Code);
        Assert.Equal(RunExit.Limited, truncated.Code);
        Assert.Equal("output_truncated", truncated.Status);
        Assert.Equal(RunExit.Denied, denied.Code);
        Assert.Equal("operator prompt denied", denied.Status);
        Assert.Equal(RunExit.Interrupted, interrupted.Code);
    }

    private static List<JsonElement> ReadEvents(string output)
    {
        var events = new List<JsonElement>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            using var document = JsonDocument.Parse(line);
            events.Add(document.RootElement.Clone());
        }

        return events;
    }

    private static HarnessSettings AssertApplied(HarnessSettings current, TaskRunSettings request)
    {
        Assert.True(TaskRunOverrides.TryApply(
            current,
            request,
            out var applied,
            out var mode,
            out var error));
        Assert.Null(mode);
        Assert.Equal(string.Empty, error);
        return applied;
    }

    private static HarnessSettings TieredContextSettings()
    {
        var provider = new ProviderDefinition(
            new ProviderName("local"),
            ProviderProtocol.Ollama,
            new Uri("http://localhost:11434/"),
            new Dictionary<string, ModelSettings>(StringComparer.Ordinal)
            {
                ["large"] = new(1_000_000, contextWindows: [272_000, 1_000_000]),
                ["small"] = new(128_000)
            },
            requiresApiKey: false);
        return new HarnessSettings(
            provider.Name,
            "large",
            ApprovalMode.Default,
            HarnessSettings.DefaultCompactionThreshold,
            ProviderCatalog.CreateStarter().Overlay([provider]));
    }

    private static TaskRunSettings WorkspaceSettings(
        RunFixture fixture,
        string workspace,
        string? workspaceTrust)
    {
        var settings = fixture.Settings("Say hello");
        return new TaskRunSettings
        {
            TaskText = settings.TaskText,
            Home = settings.Home,
            Workspace = workspace,
            Provider = settings.Provider,
            Model = settings.Model,
            Skills = settings.Skills,
            ExternalTools = settings.ExternalTools,
            Duration = settings.Duration,
            WorkspaceTrust = workspaceTrust
        };
    }

    private static TaskRunSettings Copy(
        TaskRunSettings settings,
        string? approval = null,
        bool plan = false,
        string? modelCalls = null,
        string? promptSet = null,
        string? format = null,
        bool showThinking = false,
        string? approvalModel = null,
        string? approvalProvider = null,
        string? approvalModelId = null,
        string? approvalThinking = null,
        string[]? promptAttachment = null) =>
        new()
        {
            TaskText = settings.TaskText,
            Home = settings.Home,
            Workspace = settings.Workspace,
            Provider = settings.Provider,
            Model = settings.Model,
            Skills = settings.Skills,
            ExternalTools = settings.ExternalTools,
            Duration = settings.Duration,
            Approval = approval,
            Plan = plan,
            ModelCalls = modelCalls,
            PromptSet = promptSet,
            Format = format,
            ShowThinking = showThinking,
            ApprovalModel = approvalModel,
            ApprovalProvider = approvalProvider,
            ApprovalModelId = approvalModelId,
            ApprovalThinking = approvalThinking,
            PromptAttachment = promptAttachment ?? []
        };

    private static ChatStreamEvent[] TextRound(string text, string? thinking = null)
    {
        var events = new List<ChatStreamEvent>();
        var textItem = 0;
        if (thinking is not null)
        {
            events.Add(new ChatReasoningTextDelta(0, 0, 0, ReasoningTextKind.Trace, thinking));
            textItem = 1;
        }

        events.Add(new ChatTextDelta(0, textItem, ChatRole.Assistant, text));
        events.Add(new ChatCandidateCompleted(0, FinishReason.Stop));
        return [.. events];
    }

    private static ChatStreamEvent[] ToolRound(string callId, string name, string arguments) =>
    [
        JsonChatToolCallDelta(0, 0, callId, name, arguments),
        new ChatCandidateCompleted(0, FinishReason.ToolCalls)
    ];

    private sealed class RunFixture : IDisposable
    {
        private readonly string _home;
        private readonly byte[] _configBefore;

        public RunFixture()
        {
            _home = Directory.CreateTempSubdirectory("crystal-run-home-").FullName;
            Workspace = Directory.CreateTempSubdirectory("crystal-run-workspace-").FullName;
            var crystalHome = new CrystalHome(_home);
            new DiskSettingsStore(crystalHome).LoadOrCreate();
            new WorkspaceTrustStore(crystalHome).Remember(Workspace);
            _configBefore = File.ReadAllBytes(Path.Combine(_home, "config.json"));
        }

        public string Workspace { get; }

        public string Home => _home;

        public TaskRunSettings Settings(string? task) =>
            new()
            {
                TaskText = task,
                Home = _home,
                Workspace = Workspace,
                Provider = "ollama",
                Model = "qwen3:8b",
                Skills = "off",
                ExternalTools = "off",
                Duration = "60"
            };

        public async Task<(int Code, string Output, string Error)> RunAsync(
            ScriptedRunClient client,
            string task)
        {
            return await RunAsync(client, Settings(task));
        }

        public async Task<(int Code, string Output, string Error)> RunAsync(
            ScriptedRunClient client,
            TaskRunSettings settings,
            TextReader? input = null,
            bool inputRedirected = false)
        {
            var output = new StringWriter();
            var error = new StringWriter();
            var plugins = new PluginRegistry();
            plugins.Add(new WorkspaceToolsPlugin());
            plugins.Add(new ScriptedRunPlugin(client));
            var code = await TaskRunHost.ExecuteAsync(
                settings,
                input ?? new StringReader(string.Empty),
                output,
                error,
                inputRedirected,
                plugins,
                CancellationToken.None);
            return (code, output.ToString(), error.ToString());
        }

        public HarnessSettings Reload() => new DiskSettingsStore(new CrystalHome(_home)).Load();

        public void AssertSettingsUnchanged()
        {
            Assert.Equal(_configBefore, File.ReadAllBytes(Path.Combine(_home, "config.json")));
        }

        public void Dispose()
        {
            Delete(_home);
            Delete(Workspace);
        }

        private static void Delete(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
    }

    private sealed class ScriptedRunClient : IStreamingChatClient
    {
        private readonly Queue<IReadOnlyList<ChatStreamEvent>> _rounds;
        private readonly string _review;

        public ScriptedRunClient(string review, params IReadOnlyList<ChatStreamEvent>[] rounds)
        {
            _review = review;
            _rounds = new Queue<IReadOnlyList<ChatStreamEvent>>(rounds);
        }

        public int RequestCount { get; private set; }

        public IReadOnlyList<string> ToolNames { get; private set; } = [];

        public string SystemText { get; private set; } = string.Empty;

        public IAsyncEnumerable<ChatStreamEvent> StreamAsync(
            ChatRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCount++;
            ToolNames = [.. request.Tools.Select(tool => tool.Name)];
            SystemText = request.Items.Count > 0 && request.Items[0] is ChatMessage system
                ? system.Text
                : string.Empty;
            var events = _rounds.Count == 0
                ? Array.Empty<ChatStreamEvent>()
                : _rounds.Dequeue();
            return EnumerateAsync(events, cancellationToken);
        }

        public Task<ChatResponse> CompleteAsync(
            ChatRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                new ChatResponse(
                [
                    new ChatCandidate(
                        [new ChatMessage(ChatRole.Assistant, _review)],
                        FinishReason.Stop)
                ]));
        }

        private static async IAsyncEnumerable<ChatStreamEvent> EnumerateAsync(
            IReadOnlyList<ChatStreamEvent> events,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken)
        {
            foreach (var streamEvent in events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return streamEvent;
                await Task.Yield();
            }
        }
    }

    private sealed class ScriptedRunPlugin : IPlugin
    {
        private readonly IStreamingChatClient _client;

        public ScriptedRunPlugin(IStreamingChatClient client)
        {
            _client = client;
        }

        public string Name => "scripted";

        public PluginContribution Contribute() =>
            new(clients: [new ScriptedRunFactory(_client)]);
    }

    private sealed class ScriptedRunFactory : IChatClientFactory
    {
        private readonly IStreamingChatClient _client;

        public ScriptedRunFactory(IStreamingChatClient client)
        {
            _client = client;
        }

        public bool CanCreate(ProviderProtocol protocol)
        {
            ArgumentNullException.ThrowIfNull(protocol);
            return protocol == ProviderProtocol.Ollama;
        }

        public IStreamingChatClient Create(HarnessSettings settings, string apiKey)
        {
            ArgumentNullException.ThrowIfNull(settings);
            return _client;
        }
    }
}
