using Crystal.Chat;
using Crystal.Tools;

using CrystalCode.Engine.Approvals;
using CrystalCode.Plugins.Approvals;
using CrystalCode.Plugins.Hooks;

namespace CrystalCode.Engine.Plugins.Disk;

/// <summary>
/// Runs plugin hooks in load order. A hook that throws is skipped and reported.
/// </summary>
internal sealed class PluginHookPipeline
{
    private readonly IReadOnlyList<IPluginHook> _hooks;
    private readonly Action<string>? _report;
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

    public PluginHookPipeline(IReadOnlyList<IPluginHook> hooks, Action<string>? report = null)
    {
        ArgumentNullException.ThrowIfNull(hooks);
        _hooks = hooks;
        _report = report;
    }

    public static PluginHookPipeline Empty { get; } = new([]);

    public async ValueTask StartAsync(PluginSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        foreach (var hook in _hooks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await hook.OnSessionStartedAsync(session, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Report(hook, "session-start", exception.Message);
            }
        }
    }

    public async ValueTask EndAsync(PluginSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        foreach (var hook in _hooks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await hook.OnSessionEndedAsync(session, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Report(hook, "session-end", exception.Message);
            }
        }
    }

    public string OnPrompt(string mode, string instructions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mode);
        ArgumentNullException.ThrowIfNull(instructions);
        var prompt = new PluginPrompt(mode, instructions);
        var parts = new List<string>();
        foreach (var hook in _hooks)
        {
            try
            {
                var text = hook.OnPrompt(prompt);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    parts.Add(text.Trim());
                }
            }
            catch (Exception exception)
            {
                Report(hook, "prompt", exception.Message);
            }
        }

        return string.Join("\n\n", parts);
    }

    public async ValueTask<string> OnUserMessageAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        var current = text;
        foreach (var hook in _hooks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var next = await hook.OnUserMessageAsync(new PluginUserMessage(current), cancellationToken);
                if (!string.IsNullOrWhiteSpace(next))
                {
                    current = next.Trim();
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Report(hook, "user-message", exception.Message);
            }
        }

        return current;
    }

    public async ValueTask OnTurnStartedAsync(PluginTurn turn, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(turn);
        foreach (var hook in _hooks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await hook.OnTurnStartedAsync(turn, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Report(hook, "turn-start", exception.Message);
            }
        }
    }

    public async ValueTask OnTurnFinishedAsync(PluginTurn turn, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(turn);
        foreach (var hook in _hooks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await hook.OnTurnFinishedAsync(turn, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Report(hook, "turn-end", exception.Message);
            }
        }
    }

    public async Task<IReadOnlyList<ChatItem>> PrepareModelAsync(
        PluginModelPurpose purpose,
        IReadOnlyList<ChatItem> items,
        IReadOnlyDictionary<int, string> mediaTypes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(mediaTypes);
        if (_hooks.Count == 0)
        {
            return items;
        }

        var projection = ModelHookTranscript.Project(items, mediaTypes);
        var current = projection.Items;
        foreach (var hook in _hooks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var next = await hook.RebuildModelAsync(
                    new PluginModelRequest(purpose, current),
                    cancellationToken);
                if (next is null)
                {
                    continue;
                }

                if (!ModelHookTranscript.TryAcceptRebuild(projection, current, next, out var reason))
                {
                    Report(hook, "model-items", reason);
                    continue;
                }

                current = next;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Report(hook, "model-items", exception.Message);
            }
        }

        foreach (var hook in _hooks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var next = await hook.TransformModelAsync(
                    new PluginModelRequest(purpose, current),
                    cancellationToken);
                if (next is null)
                {
                    continue;
                }

                if (!ModelHookTranscript.TryAcceptTransform(current, next, out var reason))
                {
                    Report(hook, "model-request", reason);
                    continue;
                }

                current = next;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Report(hook, "model-request", exception.Message);
            }
        }

        return ModelHookTranscript.Apply(projection, current);
    }

    public async ValueTask<ToolCall> OnToolCallAsync(ToolCall call, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(call);
        var current = call;
        foreach (var hook in _hooks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var next = await hook.OnToolCallAsync(current, cancellationToken);
                if (next is null)
                {
                    continue;
                }

                if (!string.Equals(next.CallId, current.CallId, StringComparison.Ordinal))
                {
                    Report(hook, "tool-call", "it returned a different call id.");
                    continue;
                }

                current = next;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Report(hook, "tool-call", exception.Message);
            }
        }

        return current;
    }

    public async ValueTask<PluginToolResult> OnToolResultAsync(
        ToolCall call,
        PluginToolResult result,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(result);
        var current = result;
        foreach (var hook in _hooks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var next = await hook.OnToolResultAsync(call, current, cancellationToken);
                if (next is not null)
                {
                    current = next;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Report(hook, "tool-result", exception.Message);
            }
        }

        return current;
    }

    public ToolClassification OnApproval(ToolCall call, ToolClassification classification)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(classification);
        var risk = classification.Risk;
        var requirePrompt = classification.RequirePrompt;
        var facts = new PluginApprovalFacts(
            risk.Value,
            classification.Authority.Value,
            classification.Summary);
        foreach (var hook in _hooks)
        {
            try
            {
                var advice = hook.OnApproval(call, facts);
                if (advice is null)
                {
                    continue;
                }

                if (advice.Risk is PluginRisk proposed)
                {
                    if (PluginRiskMap.Rank(proposed) < PluginRiskMap.Rank(risk))
                    {
                        Report(hook, "approval", "it tried to lower risk.");
                    }
                    else
                    {
                        risk = PluginRiskMap.ToRisk(proposed);
                        facts = new PluginApprovalFacts(risk.Value, facts.Authority, facts.Summary);
                    }
                }

                if (advice.RequirePrompt)
                {
                    requirePrompt = true;
                }
            }
            catch (Exception exception)
            {
                Report(hook, "approval", exception.Message);
            }
        }

        if (risk == classification.Risk && requirePrompt == classification.RequirePrompt)
        {
            return classification;
        }

        return new ToolClassification(
            risk,
            classification.Authority,
            classification.Summary,
            requirePrompt);
    }

    public string OnCompaction(PluginCompactionPhase phase, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var compaction = new PluginCompaction(phase, text);
        var parts = new List<string>();
        foreach (var hook in _hooks)
        {
            try
            {
                var extra = hook.OnCompaction(compaction);
                if (!string.IsNullOrWhiteSpace(extra))
                {
                    parts.Add(extra.Trim());
                }
            }
            catch (Exception exception)
            {
                Report(hook, "compaction", exception.Message);
            }
        }

        return string.Join("\n\n", parts);
    }

    public void ReportDetail(string detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return;
        }

        var message = detail.Trim();
        if (!_seen.Add("detail|" + message))
        {
            return;
        }

        _report?.Invoke(message);
    }

    public void ReportIgnoredImages()
    {
        if (!_seen.Add("images|text-turn"))
        {
            return;
        }

        _report?.Invoke("Plugin images were ignored because this turn returns text only.");
    }

    private void Report(IPluginHook hook, string stage, string detail)
    {
        var name = hook.GetType().Name;
        var message = string.IsNullOrWhiteSpace(detail) ? "it failed." : detail.Trim();
        if (!_seen.Add(name + "|" + stage + "|" + message))
        {
            return;
        }

        _report?.Invoke($"Plugin hook '{name}' {stage} was skipped: {message}");
    }
}
