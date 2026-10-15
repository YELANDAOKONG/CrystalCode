using Crystal.Tools;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Tests.Approvals;
using CrystalCode.Engine.Tests.Home;
using CrystalCode.Engine.Tests.Tools;
using CrystalCode.Engine.Tools;
using CrystalCode.Engine.Tools.External;
using CrystalCode.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Tools.External;

public sealed class ExternalApprovalClassifierTests
{
    [Theory]
    [InlineData("unknown", "workspace")]
    [InlineData("write", "unknown")]
    [InlineData("", "workspace")]
    [InlineData("write", " ")]
    [InlineData(null, "workspace")]
    [InlineData("write", null)]
    public async Task ClassifyAsync_InvalidField_FailsBothAxes(string? risk, string? authority)
    {
        using var root = new TemporaryWorkspace();
        var classifier = Create(new DelegateClassifier((_, _, _) => ValueTask.FromResult(
            new HostToolApprovalClassification(risk!, authority!, "Misleading explanation"))));

        var result = await classifier.ClassifyAsync(Call(), new Workspace(root.Path));

        AssertFailure(result);
        Assert.DoesNotContain("Misleading", result!.Summary, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClassifyAsync_NullOrException_FailsClosed(bool throws)
    {
        using var root = new TemporaryWorkspace();
        var classifier = Create(new DelegateClassifier((_, _, _) => throws
            ? throw new InvalidOperationException("Private failure detail")
            : ValueTask.FromResult<HostToolApprovalClassification>(null!)));

        var result = await classifier.ClassifyAsync(Call(), new Workspace(root.Path));

        AssertFailure(result);
        Assert.DoesNotContain("Private", result!.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClassifyAsync_CredentialFloor_CannotReplaceWarningWithToolSummary()
    {
        using var root = new TemporaryWorkspace();
        var classifier = Create(new DelegateClassifier((_, _, _) => ValueTask.FromResult(
            new HostToolApprovalClassification("forbidden", "privilegedEscalation", "Harmless read\u001b[2K"))), ["path"]);

        var result = await classifier.ClassifyAsync(
            new ToolCall("1", "apply_patch", """{"path":".ssh/id_rsa"}"""), new Workspace(root.Path));

        Assert.NotNull(result);
        Assert.Equal(Risk.Forbidden, result.Risk);
        Assert.Equal("External tool credential path", result.Summary);
    }

    [Fact]
    public async Task ClassifyAsync_FailedProposal_PreservesForbiddenFloor()
    {
        using var root = new TemporaryWorkspace();
        var classifier = Create(new DelegateClassifier((_, _, _) => throw new InvalidOperationException()), ["path"]);

        var result = await classifier.ClassifyAsync(
            new ToolCall("1", "apply_patch", """{"path":".ssh/id_rsa"}"""), new Workspace(root.Path));

        Assert.NotNull(result);
        Assert.Equal(Risk.Forbidden, result.Risk);
        Assert.Contains("credential path", result.Summary, StringComparison.Ordinal);
        Assert.Contains("classification failed", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClassifyAsync_InvalidProposal_PreservesPromptAndOutsideWarning()
    {
        using var root = new TemporaryWorkspace();
        var classifier = Create(new DelegateClassifier((_, _, _) => ValueTask.FromResult(
            new HostToolApprovalClassification("unknown", "workspace", "Harmless", RequirePrompt: true))), ["path"]);

        var result = await classifier.ClassifyAsync(
            new ToolCall("1", "apply_patch", """{"path":"../outside.txt"}"""), new Workspace(root.Path));

        Assert.NotNull(result);
        Assert.Equal(Risk.Privileged, result.Risk);
        Assert.Equal(Authority.PrivilegedEscalation, result.Authority);
        Assert.True(result.RequirePrompt);
        Assert.Contains("outside workspace", result.Summary, StringComparison.Ordinal);
        Assert.Contains("classification failed", result.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("Harmless", result.Summary, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClassifyAsync_InternalCancellation_FailsClosedWithoutDisablingInstance(bool cancelledTask)
    {
        using var root = new TemporaryWorkspace();
        using var internalCancellation = new CancellationTokenSource();
        internalCancellation.Cancel();
        var calls = 0;
        var classifier = Create(new DelegateClassifier((_, _, _) =>
        {
            if (Interlocked.Increment(ref calls) > 1)
            {
                return ValueTask.FromResult(new HostToolApprovalClassification("write", "workspace", "Patch"));
            }

            if (cancelledTask)
            {
                return new ValueTask<HostToolApprovalClassification>(
                    Task.FromCanceled<HostToolApprovalClassification>(internalCancellation.Token));
            }

            throw new OperationCanceledException(internalCancellation.Token);
        }));
        var workspace = new Workspace(root.Path);

        AssertFailure(await classifier.ClassifyAsync(Call(), workspace));
        var retry = await classifier.ClassifyAsync(Call(), workspace);

        Assert.NotNull(retry);
        Assert.Equal(Risk.Write, retry.Risk);
        Assert.Equal(Authority.Workspace, retry.Authority);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ClassifyAsync_CallerCancellation_PropagatesWithoutPromptAndDisablesInstance()
    {
        using var root = new TemporaryWorkspace();
        using var home = new TemporaryHome();
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var classifier = Create(new DelegateClassifier(async (_, _, token) =>
        {
            Interlocked.Increment(ref calls);
            started.SetResult(token);
            try
            {
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
                return new HostToolApprovalClassification("write", "workspace", "Patch");
            }
            finally
            {
                finished.SetResult();
            }
        }));
        var prompt = new RecordingApprovalPrompt(ApprovalChoice.AllowOnce);
        var policy = new ApprovalPolicy(ApprovalMode.Default, new Workspace(root.Path), new GrantStore(home.Home),
            prompt, classifiers: [classifier]);
        var pending = policy.DecideAsync(Call(), cancellation.Token).AsTask();
        var token = await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(token.IsCancellationRequested);
        Assert.Equal(0, prompt.Count);
        AssertFailure(await classifier.ClassifyAsync(Call(), new Workspace(root.Path)));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ClassifyAsync_BlockingClassifier_TimesOutBeforeTaskReturnAndDisablesInstance()
    {
        using var root = new TemporaryWorkspace();
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var block = new ManualResetEventSlim();
        var classifier = Create(new DelegateClassifier((_, _, _) =>
        {
            Interlocked.Increment(ref calls);
            try
            {
                block.Wait();
                throw new InvalidOperationException("Late failure must be observed");
            }
            finally
            {
                finished.SetResult();
            }
        }));
        try
        {
            var result = await classifier.ClassifyAsync(Call(), new Workspace(root.Path))
                .AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            AssertFailure(result);
            AssertFailure(await classifier.ClassifyAsync(Call(), new Workspace(root.Path)));
            Assert.Equal(1, calls);
        }
        finally
        {
            block.Set();
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        AssertFailure(await classifier.ClassifyAsync(Call(), new Workspace(root.Path)));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ClassifyAsync_LateAsyncResult_IsDiscardedAndCannotReenableInstance()
    {
        using var root = new TemporaryWorkspace();
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var output = new TaskCompletionSource<HostToolApprovalClassification>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var classifier = Create(new DelegateClassifier(async (_, _, token) =>
        {
            Interlocked.Increment(ref calls);
            started.SetResult(token);
            try
            {
                return await output.Task;
            }
            finally
            {
                finished.SetResult();
            }
        }));
        try
        {
            var pending = classifier.ClassifyAsync(Call(), new Workspace(root.Path)).AsTask();
            var token = await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            AssertFailure(await pending.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.True(token.IsCancellationRequested);
        }
        finally
        {
            output.TrySetResult(new HostToolApprovalClassification("write", "workspace", "Late success"));
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        AssertFailure(await classifier.ClassifyAsync(Call(), new Workspace(root.Path)));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task DecideAsync_ClassifierFailure_ExistingGrantCannotPass()
    {
        using var root = new TemporaryWorkspace();
        using var home = new TemporaryHome();
        var workspace = new Workspace(root.Path);
        var grants = new GrantStore(home.Home);
        grants.Remember(root.Path, Call(), GrantScope.Session);
        var classifier = Create(new DelegateClassifier((_, _, _) => throw new InvalidOperationException()));
        var prompt = new RecordingApprovalPrompt(ApprovalChoice.Deny);
        var policy = new ApprovalPolicy(ApprovalMode.Full, workspace, grants, prompt, classifiers: [classifier],
            automaticTools: new HashSet<string> { "apply_patch" });

        var result = await policy.DecideAsync(Call());

        Assert.Equal(ToolInvocationAction.Reject, result.Action);
        Assert.Equal(1, prompt.Count);
        AssertFailure(prompt.LastClassification);
    }

    [Fact]
    public async Task DecideAsync_WorkspacePatch_FullStillPasses()
    {
        using var root = new TemporaryWorkspace();
        using var home = new TemporaryHome();
        var classifier = Create(new DelegateClassifier((_, _, _) => ValueTask.FromResult(
            new HostToolApprovalClassification("write", "workspace", "Patch"))));
        var prompt = new RecordingApprovalPrompt(ApprovalChoice.Deny);
        var policy = new ApprovalPolicy(ApprovalMode.Full, new Workspace(root.Path), new GrantStore(home.Home),
            prompt, classifiers: [classifier]);

        var result = await policy.DecideAsync(Call());

        Assert.Equal(ToolInvocationAction.Execute, result.Action);
        Assert.Equal(0, prompt.Count);
        Assert.Equal(ApprovalPassReason.Policy, prompt.LastPassReason);
    }

    [Fact]
    public async Task DecideAsync_WorkspaceThenOutsidePatch_GrantCannotBypassExternalClassifier()
    {
        using var root = new TemporaryWorkspace();
        using var home = new TemporaryHome();
        var classifier = Create(new DelegateClassifier((call, _, _) => ValueTask.FromResult(
            new HostToolApprovalClassification("write",
                call.Arguments.Contains("../", StringComparison.Ordinal) ? "outsideWorkspace" : "workspace", "Patch"))));
        var grants = new GrantStore(home.Home);
        var workspace = new Workspace(root.Path);
        var allow = new ApprovalPolicy(ApprovalMode.Default, workspace, grants,
            new RecordingApprovalPrompt(ApprovalChoice.AllowSession), classifiers: [classifier]);
        Assert.Equal(ToolInvocationAction.Execute, (await allow.DecideAsync(Call())).Action);
        var deny = new RecordingApprovalPrompt(ApprovalChoice.Deny);
        var full = new ApprovalPolicy(ApprovalMode.Full, workspace, grants, deny, classifiers: [classifier]);

        var result = await full.DecideAsync(new ToolCall("2", "apply_patch", """{"patch":"../outside.txt"}"""));

        Assert.Equal(ToolInvocationAction.Reject, result.Action);
        Assert.Equal(1, deny.Count);
        Assert.Equal(Authority.OutsideWorkspace, deny.LastClassification!.Authority);
    }

    [Fact]
    public void TryClassify_AsyncClassifier_CannotBeSkippedBySynchronousConsumer()
    {
        using var root = new TemporaryWorkspace();
        var classifier = Create(new DelegateClassifier((_, _, _) => throw new InvalidOperationException()));

        Assert.True(classifier.TryClassify(Call(), new Workspace(root.Path), out var result));
        AssertFailure(result);
    }

    private static ToolCall Call() => new("1", "apply_patch", "{}");

    private static void AssertFailure(ToolClassification? result)
    {
        Assert.NotNull(result);
        Assert.Equal(Risk.Privileged, result.Risk);
        Assert.Equal(Authority.PrivilegedEscalation, result.Authority);
        Assert.Equal("External tool approval classification failed", result.Summary);
    }

    private static ExternalApprovalClassifier Create(IHostToolApprovalClassifier classifier, IReadOnlyList<string>? paths = null) =>
        new(new Dictionary<string, ExternalToolSpec>
        {
            ["apply_patch"] = new("apply_patch", "Patch", ToolSchema.Parse("""{"type":"object"}"""),
                ExternalCatalogSelection.Both, pathArguments: paths)
        }, new Dictionary<string, IHostToolApprovalClassifier> { ["apply_patch"] = classifier });

    private sealed class DelegateClassifier(
        Func<ToolCall, ToolHostContext, CancellationToken, ValueTask<HostToolApprovalClassification>> classify)
        : IHostToolApprovalClassifier
    {
        public ValueTask<HostToolApprovalClassification> ClassifyApprovalAsync(
            ToolCall call, ToolHostContext context, CancellationToken cancellationToken = default) =>
            classify(call, context, cancellationToken);
    }
}
