using Crystal;
using Crystal.Chat;
using Crystal.Tools;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Plugins.Disk;
using CrystalCode.Engine.Sessions;
using CrystalCode.Plugins.Approvals;
using CrystalCode.Plugins.Hooks;

using Xunit;

namespace CrystalCode.Engine.Tests.Plugins;

public sealed class PluginHookPipelineTests
{
    [Fact]
    public void OnPrompt_JoinsHookTextInOrder()
    {
        var notes = new List<string>();
        var pipeline = new PluginHookPipeline(
            [new TextHook("one"), new TextHook("two"), new ThrowingHook()],
            notes.Add);

        var text = pipeline.OnPrompt("work", "instructions");

        Assert.Equal("one\n\ntwo", text);
        Assert.Contains(notes, note => note.Contains("prompt", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BeforeTool_KeepsTheCallIdAndAppliesLaterRewrites()
    {
        var pipeline = new PluginHookPipeline(
        [
            new RewriteHook("{\"a\":1}", changeId: true),
            new RewriteHook("{\"a\":2}", changeId: false)
        ]);
        var call = new ToolCall("call-1", "read", "{}");

        var next = await pipeline.OnToolCallAsync(call, CancellationToken.None);

        Assert.Equal("call-1", next.CallId);
        Assert.Equal("{\"a\":2}", next.Arguments);
    }

    [Fact]
    public void Advise_RaisesRiskAndIgnoresALowerValue()
    {
        var notes = new List<string>();
        var pipeline = new PluginHookPipeline(
            [new AdviceHook(PluginRisk.Read, requirePrompt: false), new AdviceHook(PluginRisk.Privileged, requirePrompt: true)],
            notes.Add);
        var call = new ToolCall("1", "read", "{}");
        var classification = new ToolClassification(Risk.Write, Authority.Workspace, "Write file");

        var advised = pipeline.OnApproval(call, classification);

        Assert.Equal(Risk.Privileged, advised.Risk);
        Assert.True(advised.RequirePrompt);
        Assert.Contains(notes, note => note.Contains("lower risk", StringComparison.Ordinal));
    }

    [Fact]
    public void OnCompaction_ReturnsOnlyTheAddition()
    {
        var pipeline = new PluginHookPipeline([new CompactionHook("keep this fact")]);

        var extra = pipeline.OnCompaction(PluginCompactionPhase.Summary, "summary");

        Assert.Equal("keep this fact", extra);
    }

    [Fact]
    public async Task Executor_ApprovesTheRewrittenCall()
    {
        var inner = new RecordingExecutor();
        var pipeline = new PluginHookPipeline([new RewriteHook("{\"path\":\"b.txt\"}", changeId: false)]);
        var executor = new PluginToolExecutor(inner, pipeline);

        var results = await executor.ExecuteAsync(
            [new ToolCall("1", "read", "{\"path\":\"a.txt\"}")]);

        Assert.Equal("{\"path\":\"b.txt\"}", inner.Seen[0].Arguments);
        Assert.Equal("seen", results[0].Text);
    }

    [Fact]
    public async Task OnUserMessage_SkipsBlankTextAndAppliesALaterReplacement()
    {
        var blank = new PluginHookPipeline([new UserTextHook("  ")]);
        var pipeline = new PluginHookPipeline(
            [new UserTextHook("  "), new UserTextHook("revised")]);

        Assert.Equal("hello", await blank.OnUserMessageAsync("hello", CancellationToken.None));
        Assert.Equal("revised", await pipeline.OnUserMessageAsync("hello", CancellationToken.None));
    }

    [Fact]
    public async Task PrepareModel_DropsAnImageAndLeavesTheStoredResult()
    {
        var dropped = ImageMarkerText.Tag(1);
        var kept = ImageMarkerText.Tag(2);
        var stored = "shot " + dropped + " " + kept;
        IReadOnlyList<ChatItem> items =
        [
            new ChatMessage(ChatRole.System, "work"),
            new ToolCall("c1", "screen", "{}"),
            new ToolResult("c1", stored)
        ];
        var pipeline = new PluginHookPipeline([new DropImageHook(1)]);

        var sent = await pipeline.PrepareModelAsync(
            PluginModelPurpose.Work,
            items,
            new Dictionary<int, string> { [1] = "image/png", [2] = "image/png" },
            acceptsImages: true,
            CancellationToken.None);

        var result = Assert.IsType<ToolResult>(sent[2]);
        Assert.DoesNotContain(dropped, result.Text, StringComparison.Ordinal);
        Assert.Contains(kept, result.Text, StringComparison.Ordinal);
        Assert.Equal(stored, Assert.IsType<ToolResult>(items[2]).Text);
    }

    [Fact]
    public async Task PrepareModel_RawHookMaySplitACallFromItsResult()
    {
        var notes = new List<string>();
        IReadOnlyList<ChatItem> items =
        [
            new ChatMessage(ChatRole.System, "work"),
            new ToolCall("c1", "screen", "{}"),
            new ToolResult("c1", "shot")
        ];
        var pipeline = new PluginHookPipeline([], notes.Add, [new DropCallsHook()]);

        var sent = await pipeline.PrepareModelAsync(
            PluginModelPurpose.Work,
            items,
            new Dictionary<int, string>(),
            acceptsImages: false,
            CancellationToken.None);

        Assert.Equal(2, sent.Count);
        Assert.IsType<ToolResult>(sent[1]);
        Assert.Empty(notes);
    }

    [Fact]
    public async Task PrepareModel_RawHookMayRewriteTheSystemPromptAndRolesWithoutTouchingTheStoredItems()
    {
        IReadOnlyList<ChatItem> items =
        [
            new ChatMessage(ChatRole.System, "work"),
            new ChatMessage(ChatRole.User, "hello")
        ];
        var pipeline = new PluginHookPipeline([], rawHooks: [new RewriteMessagesHook()]);

        var sent = await pipeline.PrepareModelAsync(
            PluginModelPurpose.Work,
            items,
            new Dictionary<int, string>(),
            acceptsImages: false,
            CancellationToken.None);

        var system = Assert.IsType<ChatMessage>(sent[0]);
        Assert.Equal(ChatRole.System, system.Role);
        Assert.Equal("replaced prompt", system.Text);
        var user = Assert.IsType<ChatMessage>(sent[1]);
        Assert.Equal(ChatRole.Assistant, user.Role);
        Assert.Equal("hello", user.Text);
        Assert.Equal("work", Assert.IsType<ChatMessage>(items[0]).Text);
        Assert.Equal(ChatRole.User, Assert.IsType<ChatMessage>(items[1]).Role);
    }

    [Fact]
    public async Task PrepareModel_RawHookCanAddItemsOfEveryKind()
    {
        IReadOnlyList<ChatItem> items = [new ChatMessage(ChatRole.System, "work")];
        var pipeline = new PluginHookPipeline([], rawHooks: [new AddItemsHook()]);

        var sent = await pipeline.PrepareModelAsync(
            PluginModelPurpose.Work,
            items,
            new Dictionary<int, string>(),
            acceptsImages: false,
            CancellationToken.None);

        Assert.Equal(5, sent.Count);
        Assert.Equal("note", Assert.IsType<ChatMessage>(sent[1]).Text);
        var call = Assert.IsType<ToolCall>(sent[2]);
        Assert.Equal("c9", call.CallId);
        Assert.Equal("read", call.Name);
        var result = Assert.IsType<ToolResult>(sent[3]);
        Assert.Equal("c9", result.CallId);
        Assert.Equal(ToolResultStatus.Failure, result.Status);
        var reasoning = Assert.IsType<ChatReasoningItem>(sent[4]);
        Assert.Equal("thought", Assert.Single(reasoning.Content.TextSegments).Text);
    }

    [Fact]
    public async Task PrepareModel_RawHookRunsBeforeTransformAndTransformSeesItsItems()
    {
        var seen = new List<string>();
        IReadOnlyList<ChatItem> items = [new ChatMessage(ChatRole.System, "work")];
        var pipeline = new PluginHookPipeline(
            [new RecordingTransformHook(seen)],
            rawHooks: [new AddItemsHook()]);

        await pipeline.PrepareModelAsync(
            PluginModelPurpose.Work,
            items,
            new Dictionary<int, string>(),
            acceptsImages: false,
            CancellationToken.None);

        Assert.Equal(["0", "note", "call", "result", "think"], seen);
    }

    [Fact]
    public async Task PrepareModel_RawHookSeesWhetherTheRequestAcceptsImages()
    {
        var accepts = new List<bool>();
        IReadOnlyList<ChatItem> items = [new ChatMessage(ChatRole.System, "work")];
        var pipeline = new PluginHookPipeline([], rawHooks: [new RecordingAcceptsHook(accepts)]);
        var media = new Dictionary<int, string>();

        await pipeline.PrepareModelAsync(PluginModelPurpose.Work, items, media, true, CancellationToken.None);
        await pipeline.PrepareModelAsync(PluginModelPurpose.Side, items, media, false, CancellationToken.None);

        Assert.Equal([true, false], accepts);
    }

    [Fact]
    public async Task PrepareModel_RawHookIsSkippedWhenItRepeatsAnItemId()
    {
        var notes = new List<string>();
        IReadOnlyList<ChatItem> items = [new ChatMessage(ChatRole.System, "work")];
        var pipeline = new PluginHookPipeline([], notes.Add, [new RepeatIdHook()]);

        var sent = await pipeline.PrepareModelAsync(
            PluginModelPurpose.Work,
            items,
            new Dictionary<int, string>(),
            acceptsImages: false,
            CancellationToken.None);

        Assert.Single(sent);
        var note = Assert.Single(notes);
        Assert.Equal("Raw hook 'RepeatIdHook' model-items was skipped: it repeated an item.", note);
        Assert.True(char.IsUpper(note[0]));
    }

    [Fact]
    public async Task PrepareModel_RawHookCannotNameAnImageTheSessionDoesNotHold()
    {
        var notes = new List<string>();
        IReadOnlyList<ChatItem> items = [new ChatMessage(ChatRole.System, "work")];
        var pipeline = new PluginHookPipeline([], notes.Add, [new AddImageHook(7)]);

        var sent = await pipeline.PrepareModelAsync(
            PluginModelPurpose.Work,
            items,
            new Dictionary<int, string> { [1] = "image/png" },
            acceptsImages: true,
            CancellationToken.None);

        Assert.Single(sent);
        Assert.Contains(notes, note => note.EndsWith("it added an image.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PrepareModel_RawHookMayMoveAnAttachedImageOnlyWhenTheRequestCarriesImages()
    {
        var marker = ImageMarkerText.Tag(1);
        IReadOnlyList<ChatItem> items =
        [
            new ChatMessage(ChatRole.System, "work"),
            new ChatMessage(ChatRole.User, "look " + marker)
        ];
        var media = new Dictionary<int, string> { [1] = "image/png" };

        var carrying = await new PluginHookPipeline([], rawHooks: [new AddImageHook(1)]).PrepareModelAsync(
            PluginModelPurpose.Work,
            items,
            media,
            acceptsImages: true,
            CancellationToken.None);
        var notes = new List<string>();
        var textOnly = await new PluginHookPipeline([], notes.Add, [new AddImageHook(1)]).PrepareModelAsync(
            PluginModelPurpose.Work,
            items,
            media,
            acceptsImages: false,
            CancellationToken.None);

        Assert.Equal(3, carrying.Count);
        var moved = Assert.IsType<ChatMessage>(carrying[2]);
        Assert.Equal(marker, moved.Text);
        Assert.Equal(2, textOnly.Count);
        var note = Assert.Single(notes);
        Assert.EndsWith("it added an image to a request that cannot carry images.", note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TakeRawNote_DescribesAChangedTurnRequestOnceAndIgnoresOtherPurposes()
    {
        IReadOnlyList<ChatItem> items =
        [
            new ChatMessage(ChatRole.System, "work"),
            new ToolCall("c1", "screen", "{}"),
            new ToolResult("c1", "shot")
        ];
        var pipeline = new PluginHookPipeline([], rawHooks: [new DropCallsHook()]);
        var media = new Dictionary<int, string>();

        await pipeline.PrepareModelAsync(PluginModelPurpose.Side, items, media, false, CancellationToken.None);
        Assert.Null(pipeline.TakeRawNote());

        await pipeline.PrepareModelAsync(PluginModelPurpose.Work, items, media, false, CancellationToken.None);
        var note = pipeline.TakeRawNote();

        Assert.Equal("Raw hook 'DropCallsHook' changed this model request. The failure may be related.", note);
        Assert.True(char.IsUpper(note[0]));
        Assert.Null(pipeline.TakeRawNote());
    }

    [Fact]
    public async Task TakeRawNote_IsClearWhenTheRawHookKeptTheRequest()
    {
        IReadOnlyList<ChatItem> items = [new ChatMessage(ChatRole.System, "work")];
        var pipeline = new PluginHookPipeline([], rawHooks: [new KeepHook()]);

        await pipeline.PrepareModelAsync(
            PluginModelPurpose.Work,
            items,
            new Dictionary<int, string>(),
            acceptsImages: false,
            CancellationToken.None);

        Assert.Null(pipeline.TakeRawNote());
    }

    [Fact]
    public async Task PrepareModel_TransformStillRejectsAReorder()
    {
        var notes = new List<string>();
        IReadOnlyList<ChatItem> items =
        [
            new ChatMessage(ChatRole.System, "work"),
            new ChatMessage(ChatRole.User, "one"),
            new ChatMessage(ChatRole.User, "two")
        ];
        var pipeline = new PluginHookPipeline([new ReverseTransformHook()], notes.Add);

        var sent = await pipeline.PrepareModelAsync(
            PluginModelPurpose.Work,
            items,
            new Dictionary<int, string>(),
            acceptsImages: false,
            CancellationToken.None);

        Assert.Equal("one", Assert.IsType<ChatMessage>(sent[1]).Text);
        Assert.Contains(notes, note => note.Contains("model-request", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OnModelResponse_SkipsAThrowingHookAndKeepsTheReturnedText()
    {
        var notes = new List<string>();
        var seen = new List<PluginModelResponse>();
        IReadOnlyList<ChatItem> items = [new ChatMessage(ChatRole.Assistant, "done")];
        var pipeline = new PluginHookPipeline(
            [new ThrowingResponseHook(), new RecordingResponseHook(seen)],
            notes.Add);

        await pipeline.OnModelResponseAsync(
            PluginModelPurpose.Side,
            FinishReason.Stop,
            items,
            new TokenUsage(3, 1),
            new Dictionary<int, string>(),
            CancellationToken.None);

        var response = Assert.Single(seen);
        Assert.Equal(PluginModelPurpose.Side, response.Purpose);
        Assert.Equal(FinishReason.Stop, response.FinishReason);
        Assert.NotNull(response.Usage);
        Assert.Equal(3, response.Usage.InputTokenCount);
        Assert.Equal(1, response.Usage.OutputTokenCount);
        var message = Assert.IsType<PluginModelMessage>(Assert.Single(response.Items));
        Assert.Equal(ChatRole.Assistant, message.Role);
        Assert.Equal("done", message.Text);
        Assert.Equal("done", Assert.IsType<ChatMessage>(items[0]).Text);
        Assert.Contains(notes, note => note.Contains("model-response", StringComparison.Ordinal));
    }

    private sealed class TextHook(string text) : IPluginHook
    {
        public string? OnPrompt(PluginPrompt prompt) => text;
    }

    private sealed class ThrowingHook : IPluginHook
    {
        public string? OnPrompt(PluginPrompt prompt) =>
            throw new InvalidOperationException("prompt hook failed.");
    }

    private sealed class RewriteHook(string arguments, bool changeId) : IPluginHook
    {
        public ValueTask<ToolCall?> OnToolCallAsync(ToolCall call, CancellationToken cancellationToken = default)
        {
            var id = changeId ? call.CallId + "-other" : call.CallId;
            return ValueTask.FromResult<ToolCall?>(new ToolCall(id, call.Name, arguments));
        }
    }

    private sealed class AdviceHook(PluginRisk risk, bool requirePrompt) : IPluginHook
    {
        public PluginApprovalAdvice? OnApproval(ToolCall call, PluginApprovalFacts facts) =>
            new(risk, requirePrompt);
    }

    private sealed class CompactionHook(string text) : IPluginHook
    {
        public string? OnCompaction(PluginCompaction compaction) => text;
    }

    private sealed class UserTextHook(string text) : IPluginHook
    {
        public ValueTask<string?> OnUserMessageAsync(
            PluginUserMessage message,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<string?>(text);
    }

    private sealed class DropImageHook(int number) : IPluginHook
    {
        public ValueTask<IReadOnlyList<PluginModelItem>?> TransformModelAsync(
            PluginModelRequest request,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<PluginModelItem> next =
            [
                .. request.Items.Select(item => item switch
                {
                    PluginModelToolResult result => new PluginModelToolResult(
                        result.Id,
                        result.CallId,
                        result.Name,
                        result.Text,
                        result.Success,
                        result.Images.Where(image => image.Number != number)),
                    _ => item
                })
            ];
            return ValueTask.FromResult<IReadOnlyList<PluginModelItem>?>(next);
        }
    }

    private sealed class ThrowingResponseHook : IPluginHook
    {
        public ValueTask OnModelResponseAsync(
            PluginModelResponse response,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("response hook failed.");
    }

    private sealed class RecordingResponseHook(List<PluginModelResponse> seen) : IPluginHook
    {
        public ValueTask OnModelResponseAsync(
            PluginModelResponse response,
            CancellationToken cancellationToken = default)
        {
            seen.Add(response);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class DropCallsHook : IPluginRawHook
    {
        public ValueTask<IReadOnlyList<PluginModelItem>?> RebuildModelAsync(
            PluginModelRequest request,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<PluginModelItem> next =
                [.. request.Items.Where(item => item is not PluginModelToolCall)];
            return ValueTask.FromResult<IReadOnlyList<PluginModelItem>?>(next);
        }
    }

    private sealed class KeepHook : IPluginRawHook
    {
        public ValueTask<IReadOnlyList<PluginModelItem>?> RebuildModelAsync(
            PluginModelRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<PluginModelItem>?>(request.Items);
    }

    private sealed class RewriteMessagesHook : IPluginRawHook
    {
        public ValueTask<IReadOnlyList<PluginModelItem>?> RebuildModelAsync(
            PluginModelRequest request,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<PluginModelItem> next =
            [
                .. request.Items.Select(item => item switch
                {
                    PluginModelMessage { Role.Value: "system" } message =>
                        new PluginModelMessage(message.Id, message.Role, "replaced prompt"),
                    PluginModelMessage message =>
                        new PluginModelMessage(message.Id, ChatRole.Assistant, message.Text),
                    _ => item
                })
            ];
            return ValueTask.FromResult<IReadOnlyList<PluginModelItem>?>(next);
        }
    }

    private sealed class AddItemsHook : IPluginRawHook
    {
        public ValueTask<IReadOnlyList<PluginModelItem>?> RebuildModelAsync(
            PluginModelRequest request,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<PluginModelItem> next =
            [
                .. request.Items,
                new PluginModelMessage("note", ChatRole.User, "note"),
                new PluginModelToolCall("call", "c9", "read", "{}"),
                new PluginModelToolResult("result", "c9", "read", "failed", success: false),
                new PluginModelReasoning("think", "thought")
            ];
            return ValueTask.FromResult<IReadOnlyList<PluginModelItem>?>(next);
        }
    }

    private sealed class RecordingTransformHook(List<string> seen) : IPluginHook
    {
        public ValueTask<IReadOnlyList<PluginModelItem>?> TransformModelAsync(
            PluginModelRequest request,
            CancellationToken cancellationToken = default)
        {
            seen.AddRange(request.Items.Select(item => item.Id));
            return ValueTask.FromResult<IReadOnlyList<PluginModelItem>?>(null);
        }
    }

    private sealed class RecordingAcceptsHook(List<bool> accepts) : IPluginRawHook
    {
        public ValueTask<IReadOnlyList<PluginModelItem>?> RebuildModelAsync(
            PluginModelRequest request,
            CancellationToken cancellationToken = default)
        {
            accepts.Add(request.AcceptsImages);
            return ValueTask.FromResult<IReadOnlyList<PluginModelItem>?>(null);
        }
    }

    private sealed class RepeatIdHook : IPluginRawHook
    {
        public ValueTask<IReadOnlyList<PluginModelItem>?> RebuildModelAsync(
            PluginModelRequest request,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<PluginModelItem> next = [.. request.Items, .. request.Items];
            return ValueTask.FromResult<IReadOnlyList<PluginModelItem>?>(next);
        }
    }

    private sealed class AddImageHook(int number) : IPluginRawHook
    {
        public ValueTask<IReadOnlyList<PluginModelItem>?> RebuildModelAsync(
            PluginModelRequest request,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<PluginModelItem> next =
            [
                .. request.Items,
                new PluginModelMessage(
                    "image",
                    ChatRole.User,
                    string.Empty,
                    [new PluginModelImage(number, "image/png")])
            ];
            return ValueTask.FromResult<IReadOnlyList<PluginModelItem>?>(next);
        }
    }

    private sealed class ReverseTransformHook : IPluginHook
    {
        public ValueTask<IReadOnlyList<PluginModelItem>?> TransformModelAsync(
            PluginModelRequest request,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<PluginModelItem> next = [.. request.Items.Reverse()];
            return ValueTask.FromResult<IReadOnlyList<PluginModelItem>?>(next);
        }
    }

    private sealed class RecordingExecutor : IToolExecutor
    {
        public List<ToolCall> Seen { get; } = [];

        public IReadOnlyList<ToolDefinition> Definitions { get; } = [];

        public Task<IReadOnlyList<ToolResult>> ExecuteAsync(
            IEnumerable<ToolCall> calls,
            CancellationToken cancellationToken = default)
        {
            var list = calls.ToArray();
            Seen.AddRange(list);
            IReadOnlyList<ToolResult> results = [.. list.Select(call => new ToolResult(call.CallId, "seen"))];
            return Task.FromResult(results);
        }
    }
}
