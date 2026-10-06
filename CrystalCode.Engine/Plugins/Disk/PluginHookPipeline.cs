using Crystal;
using Crystal.Chat;
using Crystal.Tools;

using CrystalCode.Engine.Approvals;
using CrystalCode.Plugins.Approvals;
using CrystalCode.Plugins.Hooks;

namespace CrystalCode.Engine.Plugins.Disk;

/// <summary>
/// Runs plugin hooks in load order. A hook that throws is skipped and reported.
/// Raw hooks run before ordinary model hooks and are not held to their rules.
/// </summary>
internal sealed class PluginHookPipeline
{
    private readonly IReadOnlyList<IPluginHook> _hooks;
    private readonly IReadOnlyList<IPluginRawHook> _rawHooks;
    private readonly Action<string>? _report;
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly object _rawGate = new();
    private string? _rawNote;

    public PluginHookPipeline(
        IReadOnlyList<IPluginHook> hooks,
        Action<string>? report = null,
        IReadOnlyList<IPluginRawHook>? rawHooks = null)
    {
        ArgumentNullException.ThrowIfNull(hooks);
        _hooks = hooks;
        _rawHooks = rawHooks ?? [];
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

    /// <summary>
    /// Appends ordinary prompt text, then lets each raw hook replace the full
    /// system text. A raw replacement is kept as returned, including blank text.
    /// </summary>
    public string FinishPrompt(string mode, string instructions, string composed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mode);
        ArgumentNullException.ThrowIfNull(instructions);
        ArgumentNullException.ThrowIfNull(composed);
        var extra = OnPrompt(mode, instructions);
        var current = extra.Length == 0 ? composed : composed + "\n\n" + extra;
        return ReplaceText(
            current,
            "prompt",
            (hook, text) => hook.RewritePrompt(new PluginPrompt(mode, instructions), text));
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
        bool acceptsImages,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(mediaTypes);
        if (IsTurnModel(purpose))
        {
            SetRawNote(null);
        }

        if (_hooks.Count == 0 && _rawHooks.Count == 0)
        {
            return items;
        }

        var projection = ModelHookTranscript.Project(items, mediaTypes);
        var current = projection.Items;
        var rawNames = new List<string>();
        foreach (var hook in _rawHooks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var next = await hook.RebuildModelAsync(
                    new PluginModelRequest(purpose, current, acceptsImages),
                    cancellationToken);
                if (next is null)
                {
                    continue;
                }

                if (!ModelHookTranscript.TryAcceptRaw(projection, next, acceptsImages, out var reason))
                {
                    ReportRaw(hook, "model-items", reason);
                    continue;
                }

                current = next;
                rawNames.Add(hook.GetType().Name);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                ReportRaw(hook, "model-items", exception.Message);
            }
        }

        foreach (var hook in _hooks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var next = await hook.TransformModelAsync(
                    new PluginModelRequest(purpose, current, acceptsImages),
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

        var applied = ModelHookTranscript.Apply(projection, current);
        if (rawNames.Count > 0 && IsTurnModel(purpose) && Differs(items, applied))
        {
            SetRawNote(DescribeRaw(rawNames));
        }

        return applied;
    }

    /// <summary>
    /// Returns a hedged note when a raw hook changed the latest work or plan
    /// request, then clears it. The host cannot prove a failure came from the
    /// raw hook, so the note only says it may be related.
    /// </summary>
    public string? TakeRawNote()
    {
        lock (_rawGate)
        {
            var note = _rawNote;
            _rawNote = null;
            return note;
        }
    }

    public async ValueTask OnModelResponseAsync(
        PluginModelPurpose purpose,
        FinishReason finishReason,
        IReadOnlyList<ChatItem> items,
        TokenUsage? usage,
        IReadOnlyDictionary<int, string> mediaTypes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(finishReason);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(mediaTypes);
        if (_hooks.Count == 0)
        {
            return;
        }

        var projection = ModelHookTranscript.Project(items, mediaTypes);
        var response = new PluginModelResponse(purpose, finishReason, projection.Items, usage);
        foreach (var hook in _hooks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await hook.OnModelResponseAsync(response, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Report(hook, "model-response", exception.Message);
            }
        }
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
        var authority = classification.Authority;
        var summary = classification.Summary;
        var requirePrompt = classification.RequirePrompt;
        var facts = new PluginApprovalFacts(risk.Value, authority.Value, summary);
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

        foreach (var hook in _rawHooks)
        {
            try
            {
                var next = hook.RewriteApproval(call, facts, requirePrompt);
                if (next is null)
                {
                    continue;
                }

                risk = PluginRiskMap.ToRisk(next.Risk);
                authority = PluginRiskMap.ToAuthority(next.Authority);
                summary = next.Summary;
                requirePrompt = next.RequirePrompt;
                facts = new PluginApprovalFacts(risk.Value, authority.Value, summary);
            }
            catch (Exception exception)
            {
                ReportRaw(hook, "approval", exception.Message);
            }
        }

        if (risk == classification.Risk
            && authority == classification.Authority
            && summary == classification.Summary
            && requirePrompt == classification.RequirePrompt)
        {
            return classification;
        }

        return new ToolClassification(risk, authority, summary, requirePrompt);
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

    /// <summary>
    /// Appends ordinary compaction text, then lets each raw hook replace the
    /// full text. A raw replacement is kept as returned, including blank text.
    /// </summary>
    public string FinishCompaction(PluginCompactionPhase phase, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var extra = OnCompaction(phase, text);
        var current = string.IsNullOrWhiteSpace(extra) ? text : text + "\n\n" + extra.Trim();
        return ReplaceText(
            current,
            "compaction",
            (hook, value) => hook.RewriteCompaction(phase, value));
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

    private string ReplaceText(
        string current,
        string stage,
        Func<IPluginRawHook, string, string?> replace)
    {
        foreach (var hook in _rawHooks)
        {
            try
            {
                var next = replace(hook, current);
                if (next is null)
                {
                    continue;
                }

                current = next;
            }
            catch (Exception exception)
            {
                ReportRaw(hook, stage, exception.Message);
            }
        }

        return current;
    }

    private static bool IsTurnModel(PluginModelPurpose purpose) =>
        purpose == PluginModelPurpose.Work || purpose == PluginModelPurpose.Plan;

    private static bool Differs(IReadOnlyList<ChatItem> original, IReadOnlyList<ChatItem> applied)
    {
        if (original.Count != applied.Count)
        {
            return true;
        }

        for (var index = 0; index < original.Count; index++)
        {
            if (!ReferenceEquals(original[index], applied[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static string DescribeRaw(IReadOnlyList<string> names)
    {
        var quoted = string.Join(", ", names.Distinct(StringComparer.Ordinal).Select(name => $"'{name}'"));
        return names.Distinct(StringComparer.Ordinal).Count() == 1
            ? $"Raw hook {quoted} changed this model request. The failure may be related."
            : $"Raw hooks {quoted} changed this model request. The failure may be related.";
    }

    private void SetRawNote(string? note)
    {
        lock (_rawGate)
        {
            _rawNote = note;
        }
    }

    private void Report(IPluginHook hook, string stage, string detail) =>
        Write("Plugin hook", hook, stage, detail);

    private void ReportRaw(IPluginRawHook hook, string stage, string detail) =>
        Write("Raw hook", hook, stage, detail);

    private void Write(string label, object hook, string stage, string detail)
    {
        var name = hook.GetType().Name;
        var message = string.IsNullOrWhiteSpace(detail) ? "it failed." : detail.Trim();
        if (!_seen.Add(label + "|" + name + "|" + stage + "|" + message))
        {
            return;
        }

        _report?.Invoke($"{label} '{name}' {stage} was skipped: {message}");
    }
}
