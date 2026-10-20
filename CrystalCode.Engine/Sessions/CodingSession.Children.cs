using System.Text.Json;

using Crystal;
using Crystal.Agents;
using Crystal.Chat;
using Crystal.Harness;
using Crystal.Multimodal.Agents;
using Crystal.Multimodal.Harness;
using Crystal.Tools;

using CrystalCode.Engine.Compaction;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Plugins.Disk;
using CrystalCode.Engine.Prompts;
using CrystalCode.Engine.Tools;

namespace CrystalCode.Engine.Sessions;

public sealed partial class CodingSession
{
    private ToolInvocationPolicy _childPolicy = null!;
    private readonly HashSet<string> _agentNotes = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _approvalQueue = new(1, 1);
    private readonly AsyncLocal<string?> _childOrigin = new();
    private readonly AsyncLocal<Func<IReadOnlyList<ChatItem>>?> _childConversation = new();
    private readonly object _childGate = new();
    private readonly HashSet<string> _runningChildren = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ChildClients> _childClients = new(StringComparer.Ordinal);
    private readonly object _eventGate = new();
    private SemaphoreSlim _childSlots = new(4, 4);
    private IReadOnlyList<ChatItem>? _parentModelContext;
    private int _turnModelCallsBaseline;
    private int _turnToolCallsBaseline;
    private bool _childParentPlanMode;
    private bool EffectiveChildPlanMode => _childParentPlanMode || _planMode;
    private bool _parentContextEstimated;
    private SharedTurnBudget? _sharedBudget;
    private TokenUsage? _childTurnUsage;
    private bool _childUsageComplete = true;
    private TokenUsage? _parentTurnUsage;
    private readonly Dictionary<string, TokenUsage?> _childLiveUsage = new(StringComparer.Ordinal);
    private int _childModelCalls;
    private int _childToolCalls;

    private ToolCatalog WithTask(ToolCatalog catalog, bool planMode)
    {
        if (!_settings.Agents.Enabled || _settings.DisabledBuiltInTools.Contains(TaskTool.ToolName))
        {
            return catalog;
        }
        var available = AvailableAgents(planMode).ToArray();
        if (available.Length == 0)
        {
            return catalog;
        }
        var task = new TaskTool(string.Join("\n", available.Select(agent => agent.Name + ": " + agent.Description)), RunChildAsync);
        var registry = new PluginRegistry();
        registry.Add(new AgentToolsPlugin(task));
        return new ToolCatalog(catalog.Definitions.Select(definition => catalog.Find(definition.Name)!)
            .Concat(registry.CreateTools(_workspace, _todos, _frontEnd.Questions,
                planMode ? HostToolCatalog.Plan : HostToolCatalog.Work)));
    }

    private void AgentNote(string text)
    {
        lock (_childGate)
        {
            if (!_agentNotes.Add(text))
            {
                return;
            }
        }
        Note(text);
    }

    private IEnumerable<AgentDefinition> AvailableAgents(bool? planMode = null)
    {
        var catalog = AgentCatalog.Load(_home, _workspace.Root, _settings.Agents);
        foreach (var note in catalog.Notes)
        {
            AgentNote(note);
        }
        var plan = planMode ?? (_turnActive ? EffectiveChildPlanMode : _planMode);
        var planNames = plan
            ? WorkspaceCatalog.CreatePlan(_workspace, new TodoList(), _frontEnd.Questions, _plugins, _skills, _external,
                _loadedPlugins, CreateImageDescriber(), _settings.DisabledBuiltInTools).Definitions.Select(tool => tool.Name)
                .Append("view_image").ToHashSet(StringComparer.Ordinal)
            : null;
        foreach (var definition in catalog.Definitions)
        {
            if (plan && (definition.Name == "work" || definition.ManifestPath is not null && definition.Tools?.Any(name =>
                !planNames!.Contains(name)) == true))
            {
                continue;
            }
            HarnessSettings selected;
            try
            {
                selected = ResolveAgentModel(definition.Model);
            }
            catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException or InvalidOperationException)
            {
                AgentNote($"Agent '{definition.Name}' unavailable: {exception.Message}");
                continue;
            }
            if (!_credentials.TryResolve(selected.ActiveProvider, out _, out var error))
            {
                AgentNote($"Agent '{definition.Name}' unavailable: {error}");
                continue;
            }
            yield return definition;
        }
    }

    private HarnessSettings ResolveAgentModel(AgentModelSettings model)
    {
        if (model.Provider is null && model.Model is null)
        {
            if (model.Thinking is not null || model.Context is not null || model.Tier is not null || model.Speed is not null)
            {
                AgentNote("Agent model options ignored while inheriting the parent model.");
            }
            return _settings.WithThinkingEffort(_thinkingEffort);
        }
        if (string.IsNullOrWhiteSpace(model.Provider) || string.IsNullOrWhiteSpace(model.Model))
        {
            throw new ArgumentException("Agent provider and model must be specified together.", nameof(model));
        }
        var selected = _settings.WithSelection(new ProviderName(model.Provider), model.Model)
            .WithThinkingEffort(ThinkingSelection.Default);
        if (model.Thinking is string thinking)
        {
            try
            {
                var gear = ThinkingSelection.Parse(thinking);
                if (gear == ThinkingSelection.Default || gear == ThinkingSelection.Off && selected.ActiveModel.ThinkingCanDisable
                    || selected.ActiveModel.Thinking && selected.ActiveModel.AllowsEffort(gear.Value))
                {
                    selected = selected.WithThinkingEffort(gear);
                }
                else
                {
                    AgentNote($"Agent thinking '{thinking}' is unavailable; using the model default.");
                }
            }
            catch (ArgumentException)
            {
                AgentNote("Invalid Agent thinking gear ignored; using the model default.");
            }
        }
        if (model.Context is int context)
        {
            if (selected.ActiveModel.ContextWindows.Contains(context))
            {
                selected = selected.WithContextWindow(selected.Provider, selected.Model, context);
            }
            else
            {
                AgentNote("Unavailable Agent context window ignored.");
            }
        }
        if (model.Tier is string tier)
        {
            if (selected.ActiveModel.ServiceTiers.Contains(tier))
            {
                selected = selected.WithTier(selected.Provider, selected.Model, tier);
            }
            else
            {
                AgentNote("Unavailable Agent service tier ignored.");
            }
        }
        if (model.Speed is string speed)
        {
            if (selected.ActiveModel.Speeds.Contains(speed))
            {
                selected = selected.WithSpeed(selected.Provider, selected.Model, speed);
            }
            else
            {
                AgentNote("Unavailable Agent speed ignored.");
            }
        }
        return selected;
    }

    private static AgentModelSettings AgentSnapshot(HarnessSettings model) => new()
    {
        Provider = model.Provider.Value, Model = model.Model, Thinking = model.ThinkingEffort.Value,
        Context = model.ContextWindow, Tier = model.ServiceTier, Speed = model.Speed
    };

    private ChildClients GetChildClients(HarnessSettings selected)
    {
        var key = JsonSerializer.Serialize(new { selected.Provider.Value, selected.Model, selected.ServiceTier, selected.Speed, selected.MaxOutputTokens });
        lock (_childGate)
        {
            if (_childClients.TryGetValue(key, out var clients))
            {
                return clients;
            }
            var text = CreateClient(selected);
            try
            {
                clients = new ChildClients(text, CreateMultimodalClient(selected));
                _childClients.Add(key, clients);
                return clients;
            }
            catch
            {
                DisposeClient(text);
                throw;
            }
        }
    }

    private static bool ChildToolAllowed(string name) => name is not ("task" or "question"
        or "bash_start" or "bash_status" or "bash_logs" or "bash_stop");

    private ToolCatalog ChildCatalog(TodoList todos) => EffectiveChildPlanMode
        ? WorkspaceCatalog.CreatePlan(_workspace, todos, _frontEnd.Questions, _plugins, _skills, _external,
            _loadedPlugins, CreateImageDescriber(), _settings.DisabledBuiltInTools)
        : WorkspaceCatalog.CreateWork(_workspace, todos, _frontEnd.Questions, _plugins, _skills, _external,
            _settings.BashTimeoutSeconds, _loadedPlugins, CreateImageDescriber(),
            disabledBuiltInTools: _settings.DisabledBuiltInTools);

    private IReadOnlyList<Crystal.Multimodal.Tools.IMultimodalTool> ChildImageTools(TodoList todos)
    {
        var catalog = EffectiveChildPlanMode ? HostToolCatalog.Plan : HostToolCatalog.Work;
        var tools = _plugins.CreateMultimodalTools(_workspace, todos, _frontEnd.Questions, catalog).ToList();
        tools.AddRange(EffectiveChildPlanMode ? _loadedPlugins.PlanMultimodalTools : _loadedPlugins.WorkMultimodalTools);
        tools.AddRange(EffectiveChildPlanMode ? _external.PlanMultimodalTools : _external.WorkMultimodalTools);
        tools.AddRange(ViewImageTool.ForSession(_workspace, sessionAcceptsImages: true));
        return tools.Where(tool => !_settings.DisabledBuiltInTools.Contains(tool.Definition.Name)).ToArray();
    }

    private async Task<string> RunChildAsync(string agent, string label, string prompt, string? sessionId,
        string parentCallId, CancellationToken cancellationToken)
    {
        var definition = AvailableAgents().FirstOrDefault(item => item.Name == agent)
            ?? throw new ArgumentException("The requested Agent is not available.", nameof(agent));
        ChildSessionDocument child;
        var invocation = Guid.NewGuid();
        var todos = new TodoList();
        var catalog = ChildCatalog(todos);
        lock (_childGate)
        {
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                if (!_children.TryGet(sessionId, out child!))
                {
                    throw new ArgumentException("The child session does not belong to this parent.", nameof(sessionId));
                }
                if (child.Agent != agent)
                {
                    throw new ArgumentException("The child session belongs to a different Agent.", nameof(agent));
                }
            }
            else
            {
                var selected = ResolveAgentModel(definition.Model);
                var tools = catalog.Definitions.Select(tool => tool.Name)
                    .Concat(selected.ActiveModel.ImageInput ? ChildImageTools(todos).Select(tool => tool.Definition.Name) : [])
                    .Where(name => ChildToolAllowed(name) && (definition.Tools is null || definition.Tools.Contains(name)))
                    .Distinct(StringComparer.Ordinal).ToArray();
                child = new ChildSessionDocument
                {
                    Id = SessionStore.NewId(), Agent = agent, Label = label, Model = AgentSnapshot(selected),
                    SystemPrompt = definition.Prompt, Tools = tools
                };
            }
            if (!_runningChildren.Add(child.Id))
            {
                throw new InvalidOperationException("This child session is already running.");
            }
            child = child with { Status = "queued", ParentCallId = parentCallId };
            _children.Set(child);
        }
        var entered = false;
        TokenUsage? settledUsage = null;
        todos.Replace(SessionMapper.ReadTodos(child.Todos));
        TurnResult? result = null;
        var archive = TranscriptCodec.Read(child.Archive.ToList());
        var notes = child.Notes.ToList();
        var images = new Dictionary<int, ImageAttachment>(_sessionStore.ReadImages(child.Images));
        var extraUsage = new UsageAccumulator();
        var extraModelCalls = 0;
        var latestUsage = SessionMapper.ReadUsage(child.Usage);
        var usageAfterCompaction = latestUsage;
        void Emit(SessionEvent value)
        {
            if (value is ContextEstimated estimate)
            {
                notes.Add($"Estimated context: ~{estimate.Tokens} tokens");
            }
            if (value is ErrorWritten error)
            {
                notes.Add(error.Text);
            }
            if (value is RetryScheduled retry)
            {
                notes.Add($"Retry {retry.Attempt.Attempt}: {retry.Attempt.Message}");
            }
            Publish(new ChildEventReceived(child.Id, invocation, value));
        }
        try
        {
            Publish(new ChildSessionChanged(child, invocation));
            await _childSlots.WaitAsync(cancellationToken);
            entered = true;
            _childOrigin.Value = $"{agent} / {child.Label} / {child.Id}";
            child = child with { Status = "running" };
            _children.Set(child);
            Publish(new ChildSessionChanged(child, invocation));
            if (child.Model.Provider is null || child.Model.Model is null)
            {
                throw new InvalidDataException("The saved child model snapshot is incomplete; the parent model will not be substituted.");
            }
            var selectedModel = ResolveAgentModel(child.Model)
                .WithTier(new ProviderName(child.Model.Provider!), child.Model.Model!, child.Model.Tier)
                .WithSpeed(new ProviderName(child.Model.Provider!), child.Model.Model!, child.Model.Speed);
            var clients = GetChildClients(selectedModel);
            var items = TranscriptCodec.Read(child.Items.ToList());
            var directories = DirectoryAccessText.Render(_workspace.Access.Snapshot());
            var facts = PromptContext.Create(_workspace.Root, selectedModel.Provider.Value, selectedModel.Model,
                EffectiveChildPlanMode ? "plan" : "work", string.Empty, _prompts.Instructions,
                sessionId: child.Id, approval: _approval.Value, directories: directories);
            var system = AgentPrompt.Rules + "\n\n" + child.SystemPrompt + "\n\n" + facts.EnvironmentBlock
                + "\n\nParent mode: " + (EffectiveChildPlanMode ? "Plan. Inspect and report only." : "Work.")
                + "\n\n" + _prompts.Instructions + "\n\n" + directories;
            if (child.Tools.Contains(SkillTool.ToolName) && _skills is not null)
            {
                system += "\n\n" + CrystalCode.Engine.Skills.SkillGuidance.Render(_skills);
            }
            if (items.FirstOrDefault() is ChatMessage { Role: var role } && role == ChatRole.System)
            {
                items[0] = new ChatMessage(ChatRole.System, system);
            }
            else
            {
                items.Insert(0, new ChatMessage(ChatRole.System, system));
            }
            var user = new ChatMessage(ChatRole.User, prompt);
            items.Add(user);
            archive.Add(user);
            child = child with { Items = TranscriptCodec.Write(items).ToArray(), Archive = TranscriptCodec.Write(archive).ToArray() };
            _children.Set(child);
            Publish(new ChildSessionChanged(child, invocation));
            var allowed = child.Tools.Where(ChildToolAllowed).ToHashSet(StringComparer.Ordinal);
            catalog = ChildCatalog(todos);
            var filtered = new ToolCatalog(catalog.Definitions.Where(tool => allowed.Contains(tool.Name))
                .Select(tool => catalog.Find(tool.Name)!));
            var executor = new ToolExecutor(filtered, new ToolExecutionOptions(ToolExecutionMode.Serial, 1),
                _childPolicy, HarnessExceptionMapper.MapAsync);
            var observer = new ChildTurnObserver(Emit, (context, usage) =>
            {
                latestUsage = context;
                usageAfterCompaction = context;
                UpdateChildUsage(invocation.ToString("N"), usage);
                Emit(new UsageChanged(context, usage is null ? null : SessionLedger.Add(SessionMapper.ReadUsage(child.CumulativeUsage), usage),
                    selectedModel.ContextWindow, true));
            }, _ => usageAfterCompaction = null);
            var limits = agent == "explore" ? new TurnLimits(32, 128, null) : TurnLimits.Unlimited;
            IReadOnlyList<ChatItem> reviewItems = items;
            async Task<CompactionOutcome> CompactAsync(IReadOnlyList<ChatItem> context, CancellationToken token)
            {
                reviewItems = context;
                var compactionLimits = LimitsFor(selectedModel);
                if (!ContextAccountant.ShouldCompact(TokenEstimator.Items(context), usageAfterCompaction,
                    compactionLimits.ContextWindow, compactionLimits.Threshold, compactionLimits.MaxTokens, compactionLimits.ReservedOutputTokens))
                {
                    return new CompactionOutcome(context, CompactionKind.Unchanged);
                }
                var summarySelection = _settings.CompactionModel.Enabled
                    ? selectedModel.WithSelection(new ProviderName(_settings.CompactionModel.Provider!), _settings.CompactionModel.Model!)
                        .WithThinkingEffort(_settings.CompactionModel.ThinkingEffort)
                    : selectedModel;
                var summaryClient = CreateClient(summarySelection.WithOutputTokenCap(compactionLimits.SummaryOutputCap()));
                try
                {
                    var budgeted = new ChildSummaryClient(summaryClient, _sharedBudget, () => extraModelCalls++);
                    var compactor = new ContextCompactor(budgeted, _settings.Retry.ToOptions(),
                        attempt => Emit(new RetryScheduled(attempt)),
                        systemText: () => CompactionPrompt.ComposeSystem(facts.WithMode("compaction"),
                            _placeholders, _prompts.CompactionSystem),
                        reportResponse: (response, _) =>
                        {
                            extraUsage.Add(response.Usage);
                            return Task.CompletedTask;
                        },
                        userText: (conversation, todoText, prior) => CompactionPrompt.UserText(conversation, todoText, prior,
                            CurrentPromptBinding(facts.WithMode("compaction")), _prompts.CompactionUser),
                        reasoning: summarySelection.ThinkingEffort.ToReasoningOptions(summarySelection.ActiveModel));
                    var compacted = await compactor.CompactAsync(context, todos.Format(), compactionLimits, token);
                    if (!compacted.Compacted || TokenEstimator.Items(compacted.Transcript) >= TokenEstimator.Items(context))
                    {
                        return new CompactionOutcome(context, CompactionKind.Exhausted);
                    }
                    usageAfterCompaction = null;
                    Emit(new NoteWritten("Compacted child context"));
                    return compacted;
                }
                finally
                {
                    DisposeClient(summaryClient);
                }
            }
            _childConversation.Value = () => reviewItems;
            var reasoning = selectedModel.ThinkingEffort.ToReasoningOptions(selectedModel.ActiveModel);
            var purpose = EffectiveChildPlanMode ? CrystalCode.Plugins.Hooks.PluginModelPurpose.Plan : CrystalCode.Plugins.Hooks.PluginModelPurpose.Work;
            Task<IReadOnlyList<ChatItem>> PrepareAsync(IReadOnlyList<ChatItem> context, CancellationToken token) =>
                _hooks.PrepareModelAsync(purpose, context, images.ToDictionary(pair => pair.Key, pair => pair.Value.MimeType),
                    clients.Images is not null, token);
            Task ReportAsync(ChatResponse response, CancellationToken token) => _hooks.OnModelResponseAsync(purpose,
                response.Candidates[0].FinishReason, response.Candidates[0].Items, response.Usage,
                images.ToDictionary(pair => pair.Key, pair => pair.Value.MimeType), token).AsTask();
            if (clients.Images is not null)
            {
                var imageExecutor = new PluginMultimodalExecutor(new HybridMultimodalToolExecutor(executor,
                    ChildImageTools(todos).Where(tool => allowed.Contains(tool.Definition.Name)), _childPolicy), _hooks);
                var adapter = new MultimodalChildAgent(clients.Images.Capabilities, async token =>
                {
                    var turn = new MultimodalStreamingTurn(clients.Images, imageExecutor, limits, images, observer,
                        reasoning, CompactAsync, _settings.Retry.ToOptions(), ReserveImageNumber,
                        image => { images.Add(image.Number, image); AddImage(image); },
                        () => new Dictionary<int, ImageAttachment>(images), archive.AddRange,
                        PrepareAsync, ReportAsync, _sharedBudget);
                    result = await turn.RunAsync(items, token);
                    return result;
                }, completed => MultimodalTranscript.Convert(completed.Transcript, images));
                var name = new MultimodalAgentName(agent);
                var harness = new MultimodalAgentHarness([new MultimodalAgentRegistration(name, adapter)])
                    .CreateSession(Guid.NewGuid(), new MultimodalHarnessLimits(0, null, null, null), cancellationToken);
                await harness.InvokeAsync(new MultimodalAgentInvocationRequest(invocation, name,
                    MultimodalTranscript.Convert(items, images), MultimodalAgentRunLimits.Unlimited), cancellationToken);
            }
            else
            {
                var permitted = new ChildToolExecutor(executor, allowed);
                var adapter = new ChildAgent(async (request, token) =>
                {
                    var turn = new StreamingTurn(clients.Text, new PluginToolExecutor(permitted, _hooks), limits,
                        observer, reasoning, CompactAsync, _settings.Retry.ToOptions(), archive.AddRange,
                        PrepareAsync, ReportAsync, _sharedBudget);
                    result = await turn.RunAsync(request.Items, token);
                    return result;
                });
                var harness = new AgentHarness([new AgentRegistration(new AgentName(agent), adapter)])
                    .CreateSession(Guid.NewGuid(), new HarnessLimits(0, null, null, null), cancellationToken);
                await harness.InvokeAsync(new AgentInvocationRequest(invocation, new AgentName(agent), items,
                    AgentRunLimits.Unlimited), cancellationToken);
            }
            return JsonSerializer.Serialize(new
            {
                sessionId = child.Id, status = result?.StopReason.Value ?? "failed",
                output = result?.StopReason == TurnStopReason.Completed
                    ? result.Transcript.OfType<ChatMessage>().LastOrDefault(message => message.Role == ChatRole.Assistant)?.Text
                        ?? "The child returned no final text."
                    : "Child stopped before completion. Inspect its record or continue it using sessionId."
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            notes.Add("Child execution failed.");
            Emit(new ErrorWritten(exception.Message));
            return JsonSerializer.Serialize(new { sessionId = child.Id, status = "failed", output = "Child execution failed; inspect its record." });
        }
        finally
        {
            if (result is not null)
            {
                var ledger = new SessionLedger();
                ledger.Restore(0, child.ModelCalls, child.ToolCalls, SessionMapper.ReadUsage(child.Usage),
                    SessionMapper.ReadUsage(child.CumulativeUsage));
                ledger.Record(result);
                if (extraModelCalls > 0)
                {
                    ledger.RecordChildUsage(extraModelCalls, 0, extraUsage.Build());
                }
                child = child with
                {
                    Items = TranscriptCodec.Write(result.Transcript).ToArray(),
                    Usage = SessionMapper.WriteUsage(latestUsage ?? result.Usage),
                    CumulativeUsage = SessionMapper.WriteUsage(ledger.CumulativeUsage),
                    ModelCalls = ledger.ModelCalls, ToolCalls = ledger.ToolCalls
                };
                lock (_usageGate)
                {
                    _childModelCalls += result.ModelCallCount + extraModelCalls;
                    _childToolCalls += result.ToolCallCount;
                    if (result.ModelCallCount > 0 && result.AccumulatedUsage is null
                        || extraModelCalls > 0 && extraUsage.Build() is null)
                    {
                        _childUsageComplete = false;
                    }
                    if (result.AccumulatedUsage is not null)
                    {
                        _childTurnUsage = SessionLedger.Add(_childTurnUsage, result.AccumulatedUsage);
                    }
                    if (extraUsage.Build() is { } summaryUsage)
                    {
                        _childTurnUsage = SessionLedger.Add(_childTurnUsage, summaryUsage);
                    }
                }
            }
            child = child with
            {
                Archive = TranscriptCodec.Write(archive).ToArray(), Notes = notes.ToArray(),
                Todos = SessionMapper.WriteTodos(todos.Snapshot()).ToArray(),
                Images = [.. SessionMapper.WriteImages(images.Values), .. child.Images.Where(image => !images.ContainsKey(image.Number))],
                Status = result?.StopReason.Value ?? (cancellationToken.IsCancellationRequested ? "interrupted" : "failed"),
                StopReason = result?.StopReason.Value
            };
            lock (_childGate)
            {
                _children.Set(child);
                if (result is not null)
                {
                    var reported = result.AccumulatedUsage;
                    if (extraModelCalls > 0)
                    {
                        reported = reported is not null && extraUsage.Build() is { } extra
                            ? SessionLedger.Add(reported, extra) : null;
                    }
                    settledUsage = reported;
                    lock (_usageGate)
                    {
                        _ledger.RecordChildUsage(result.ModelCallCount + extraModelCalls, result.ToolCallCount, reported);
                    }
                }
                _runningChildren.Remove(child.Id);
            }
            try
            {
                if (result is not null)
                {
                    UpdateChildUsage(invocation.ToString("N"), settledUsage);
                }
                Publish(new ChildSessionChanged(child, invocation));
                SaveSession();
            }
            finally
            {
                if (entered)
                {
                    _childSlots.Release();
                }
            }
        }
    }

    private IReadOnlyList<ChatItem> CheckpointItems(IEnumerable<ChatItem> source)
    {
        var items = source.ToList();
        var completed = items.OfType<ToolResult>().Select(result => result.CallId).ToHashSet(StringComparer.Ordinal);
        var children = _children.Snapshot();
        foreach (var call in items.OfType<ToolCall>().ToArray())
        {
            if (completed.Contains(call.CallId))
            {
                continue;
            }
            var child = call.Name == TaskTool.ToolName ? children.LastOrDefault(value => value.ParentCallId == call.CallId) : null;
            var output = child is null
                ? "Tool execution did not complete before this checkpoint. Inspect existing effects before retrying."
                : JsonSerializer.Serialize(new
                {
                    sessionId = child.Id, status = child.Status,
                    output = child.Status == "completed"
                        ? TranscriptCodec.Read(child.Archive.ToList()).OfType<ChatMessage>()
                            .LastOrDefault(message => message.Role == ChatRole.Assistant)?.Text
                        : "The child did not finish before this checkpoint. Inspect it and continue using sessionId; do not duplicate its work."
                });
            items.Add(new ToolResult(call.CallId, output, child?.Status == "completed" ? ToolResultStatus.Success : ToolResultStatus.Failure));
        }
        return items;
    }

    private TokenUsage? LiveCumulativeUsage()
    {
        var cumulative = _turnCumulativeBaseline;
        if (_parentTurnUsage is not null)
        {
            cumulative = SessionLedger.Add(cumulative, _parentTurnUsage);
        }
        foreach (var usage in _childLiveUsage.Values)
        {
            if (usage is null)
            {
                return null;
            }
            cumulative = SessionLedger.Add(cumulative, usage);
        }
        return cumulative;
    }

    private void UpdateChildUsage(string id, TokenUsage? usage)
    {
        TokenUsage? cumulative;
        TokenUsage? context;
        lock (_usageGate)
        {
            _childLiveUsage[id] = usage;
            cumulative = LiveCumulativeUsage();
            context = _shownUsage;
        }
        ShowUsage(context, cumulative, interim: true);
    }

    private void ReleaseChildClients()
    {
        lock (_childGate)
        {
            foreach (var clients in _childClients.Values)
            {
                DisposeClient(clients.Text);
                DisposeClient(clients.Images);
            }
            _childClients.Clear();
        }
    }
}
