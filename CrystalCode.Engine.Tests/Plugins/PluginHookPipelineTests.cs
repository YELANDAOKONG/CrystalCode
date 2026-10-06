using Crystal.Tools;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Plugins.Disk;
using CrystalCode.Plugins.Approvals;
using CrystalCode.Plugins.Hooks;

using Xunit;

namespace CrystalCode.Engine.Tests.Plugins;

public sealed class PluginHookPipelineTests
{
    [Fact]
    public void AppendPrompt_JoinsHookTextInOrder()
    {
        var notes = new List<string>();
        var pipeline = new PluginHookPipeline(
            [new TextHook("one"), new TextHook("two"), new ThrowingHook()],
            notes.Add);

        var text = pipeline.AppendPrompt("work", "instructions");

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

        var next = await pipeline.BeforeToolAsync(call, CancellationToken.None);

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

        var advised = pipeline.Advise(call, classification);

        Assert.Equal(Risk.Privileged, advised.Risk);
        Assert.True(advised.RequirePrompt);
        Assert.Contains(notes, note => note.Contains("lower risk", StringComparison.Ordinal));
    }

    [Fact]
    public void AppendCompaction_ReturnsOnlyTheAddition()
    {
        var pipeline = new PluginHookPipeline([new CompactionHook("keep this fact")]);

        var extra = pipeline.AppendCompaction(PluginCompactionPhase.Summary, "summary");

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

    private sealed class TextHook(string text) : IPluginHook
    {
        public string? AppendPrompt(PluginPrompt prompt) => text;
    }

    private sealed class ThrowingHook : IPluginHook
    {
        public string? AppendPrompt(PluginPrompt prompt) =>
            throw new InvalidOperationException("prompt hook failed.");
    }

    private sealed class RewriteHook(string arguments, bool changeId) : IPluginHook
    {
        public ValueTask<ToolCall?> BeforeToolAsync(ToolCall call, CancellationToken cancellationToken = default)
        {
            var id = changeId ? call.CallId + "-other" : call.CallId;
            return ValueTask.FromResult<ToolCall?>(new ToolCall(id, call.Name, arguments));
        }
    }

    private sealed class AdviceHook(PluginRisk risk, bool requirePrompt) : IPluginHook
    {
        public PluginApprovalAdvice? AdviseApproval(ToolCall call, PluginApprovalFacts facts) =>
            new(risk, requirePrompt);
    }

    private sealed class CompactionHook(string text) : IPluginHook
    {
        public string? AppendCompaction(PluginCompaction compaction) => text;
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
