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
            CancellationToken.None);

        var result = Assert.IsType<ToolResult>(sent[2]);
        Assert.DoesNotContain(dropped, result.Text, StringComparison.Ordinal);
        Assert.Contains(kept, result.Text, StringComparison.Ordinal);
        Assert.Equal(stored, Assert.IsType<ToolResult>(items[2]).Text);
    }

    [Fact]
    public async Task PrepareModel_RejectsAToolResultWithoutItsCall()
    {
        var notes = new List<string>();
        IReadOnlyList<ChatItem> items =
        [
            new ChatMessage(ChatRole.System, "work"),
            new ToolCall("c1", "screen", "{}"),
            new ToolResult("c1", "shot")
        ];
        var pipeline = new PluginHookPipeline([new DropCallsHook()], notes.Add);

        var sent = await pipeline.PrepareModelAsync(
            PluginModelPurpose.Work,
            items,
            new Dictionary<int, string>(),
            CancellationToken.None);

        Assert.Equal(3, sent.Count);
        Assert.Contains(notes, note => note.Contains("split a tool call", StringComparison.Ordinal));
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

    private sealed class DropCallsHook : IPluginHook
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
