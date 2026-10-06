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
using CrystalCode.Run;

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
            ToolRound("c1", "bash", """{"command":"printf ready > ran.txt"}"""));
        var settings = Copy(fixture.Settings("Create the marker file"), plan: true);

        var result = await fixture.RunAsync(client, settings);

        Assert.Equal(RunExit.Failed, result.Code);
        Assert.Contains("The requested tool is not registered.", result.Output, StringComparison.Ordinal);
        Assert.Contains("[Stopped] failed", result.Output, StringComparison.Ordinal);
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
            PromptSet = "default"
        };

        var applied = AssertApplied(current, request);

        Assert.Null(applied.ExecutionBudget.MaximumModelCalls);
        Assert.Equal(0, applied.ExecutionBudget.MaximumToolCalls);
        Assert.Null(applied.ExecutionBudget.MaximumDuration);
        Assert.Null(applied.BashTimeoutSeconds);
        Assert.False(applied.Skills);
        Assert.False(applied.ExternalTools);
        Assert.Equal("maximum", applied.ThinkingEffort.Value);
        Assert.Equal(ApprovalMode.Audit, applied.Approval);
        Assert.Equal(current.Provider, applied.Provider);
        Assert.Equal(TurnLimits.DefaultMaximumModelCalls, current.ExecutionBudget.MaximumModelCalls);
    }

    [Fact]
    public async Task ApprovalModelOverride_StaysProcessOnly()
    {
        using var fixture = new RunFixture();
        var client = new ScriptedRunClient(AllowReview, TextRound("Hello there."));
        var settings = Copy(
            fixture.Settings("Say hello"),
            approvalModel: "on",
            approvalModelId: "qwen3:8b");

        var result = await fixture.RunAsync(client, settings);

        Assert.Equal(RunExit.Completed, result.Code);
        fixture.AssertSettingsUnchanged();
        Assert.False(fixture.Reload().ApprovalModel.Enabled);
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

    [Fact]
    public void Exit_FailureAndBudgetOutrankDenial()
    {
        var failed = RunExit.From(TurnStopReason.Failed, operatorDenied: true);
        var limited = RunExit.From(TurnStopReason.ModelCallLimitReached, operatorDenied: true);
        var denied = RunExit.From(TurnStopReason.Completed, operatorDenied: true);
        var interrupted = RunExit.From(TurnStopReason.Interrupted, operatorDenied: true);

        Assert.Equal(RunExit.Failed, failed.Code);
        Assert.Equal(RunExit.Limited, limited.Code);
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
            out var planMode,
            out var error));
        Assert.False(planMode);
        Assert.Equal(string.Empty, error);
        return applied;
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
        string? approvalModelId = null) =>
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
            ApprovalModelId = approvalModelId
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
        new ChatToolCallDelta(0, 0, callId, name, arguments),
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
            new SettingsStore(new CrystalHome(_home)).LoadOrCreate();
            _configBefore = File.ReadAllBytes(Path.Combine(_home, "config.json"));
        }

        public string Workspace { get; }

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

        public HarnessSettings Reload() => new SettingsStore(new CrystalHome(_home)).Load();

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

        public IAsyncEnumerable<ChatStreamEvent> StreamAsync(
            ChatRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCount++;
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
