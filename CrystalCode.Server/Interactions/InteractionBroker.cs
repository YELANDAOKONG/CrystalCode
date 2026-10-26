using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;

using Crystal.Tools;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Approvals.Interfaces;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Sessions.Interfaces;
using CrystalCode.Engine.Tools;
using CrystalCode.Server.Events;
using CrystalCode.Server.Wire;

namespace CrystalCode.Server.Interactions;

/// <summary>
/// The server's answer to every engine question: approval, the question
/// tool, workspace trust, session choice, and child choice. Each becomes an
/// <see cref="Interaction"/> that waits for a client. A question raised by a
/// client's own command goes to that client; a question raised by the model
/// goes to every client with the operate scope. The engine still decides
/// what is allowed: this class only validates that an answer is one the
/// engine offered.
/// </summary>
public sealed class InteractionBroker : IApprovalPrompt, IUserPrompt, ISessionChooser, IWorkspaceTrustPrompt
{
    private readonly ConcurrentDictionary<string, Interaction> _pending = new(StringComparer.Ordinal);
    private readonly EventHub _hub;
    private Func<string?> _origin = static () => null;
    private Func<string, bool> _connected = static _ => false;

    public InteractionBroker(EventHub hub)
    {
        ArgumentNullException.ThrowIfNull(hub);
        _hub = hub;
    }

    /// <summary>Reads the origin of the engine call that is asking.</summary>
    public void BindOrigin(Func<string?> origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        _origin = origin;
    }

    /// <summary>Reports whether a client still has an open event stream.</summary>
    public void BindConnections(Func<string, bool> connected)
    {
        ArgumentNullException.ThrowIfNull(connected);
        _connected = connected;
    }

    public IReadOnlyList<Interaction> Pending =>
        [.. _pending.Values.Where(static item => !item.IsDone).OrderBy(static item => item.Id, StringComparer.Ordinal)];

    public bool TryGet(string id, out Interaction interaction) => _pending.TryGetValue(id, out interaction!);

    /// <summary>A disconnected client's targeted questions go to every client.</summary>
    public void Disconnected(string client)
    {
        foreach (var interaction in _pending.Values)
        {
            if (!interaction.IsDone && string.Equals(interaction.Target, client, StringComparison.Ordinal))
            {
                interaction.Target = null;
                PublishOpened(interaction);
            }
        }
    }

    public void CancelAll()
    {
        foreach (var interaction in _pending.Values)
        {
            Cancel(interaction);
        }
    }

    public bool TryAnswer(string id, JsonElement answer, string client, out bool conflict, out string error)
    {
        conflict = false;
        error = string.Empty;
        if (!_pending.TryGetValue(id, out var interaction) || interaction.IsDone)
        {
            conflict = true;
            error = "The interaction is no longer pending.";
            return false;
        }

        if (interaction.Target is { } target && !string.Equals(target, client, StringComparison.Ordinal))
        {
            conflict = true;
            error = "The interaction belongs to another client.";
            return false;
        }

        if (!interaction.TryResolve(answer, out error))
        {
            conflict = error.Length == 0;
            if (conflict)
            {
                error = "The interaction is no longer pending.";
            }

            return false;
        }

        _pending.TryRemove(id, out _);
        _hub.Publish("interaction.resolved", null, writer =>
        {
            writer.WriteString("id", id);
            writer.WriteString("kind", interaction.Kind);
            writer.WriteString("by", client);
        });
        return true;
    }

    public ValueTask<ApprovalChoice> AskAsync(
        ToolCall call,
        ToolClassification classification,
        ApprovalReviewVerdict? review = null,
        ApprovalReviewVerdict? extendedReview = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(classification);
        var reusable = classification.AllowsReusableGrant;
        string[] choices = reusable ? ["once", "session", "always", "deny"] : ["once", "deny"];
        return OpenAsync<ApprovalChoice>(
            InteractionKind.Approval,
            writer =>
            {
                writer.WriteStartObject("call");
                WireWriter.Call(writer, call);
                writer.WriteEndObject();
                WireWriter.Classification(writer, "classification", classification);
                WireWriter.Verdict(writer, "review", review);
                WireWriter.Verdict(writer, "extendedReview", extendedReview);
                WireWriter.Strings(writer, "choices", choices);
            },
            answer =>
            {
                var choice = Text(answer, "choice");
                return choice switch
                {
                    "once" => (true, ApprovalChoice.AllowOnce, string.Empty),
                    "session" when reusable => (true, ApprovalChoice.AllowSession, string.Empty),
                    "always" when reusable => (true, ApprovalChoice.AllowPersistent, string.Empty),
                    "deny" => (true, ApprovalChoice.Deny, string.Empty),
                    _ => (false, ApprovalChoice.Deny, "choice must be one of " + string.Join(", ", choices) + ".")
                };
            },
            ApprovalChoice.Deny,
            cancellationToken);
    }

    public void NotifyPassed(
        ToolCall call,
        ToolClassification classification,
        ApprovalPassReason reason,
        ApprovalReviewVerdict? review = null,
        ApprovalReviewVerdict? extendedReview = null)
    {
        // The engine already publishes ApprovalPassed for the stream.
        ArgumentNullException.ThrowIfNull(call);
    }

    public void NotifyReviewing(ToolCall call, ApprovalReviewStage stage)
    {
        // The engine already publishes ApprovalReviewing for the stream.
        ArgumentNullException.ThrowIfNull(call);
    }

    public ValueTask<QuestionResponse> AskAsync(
        IReadOnlyList<UserQuestion> questions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(questions);
        return OpenAsync(
            InteractionKind.Question,
            writer =>
            {
                writer.WriteStartArray("questions");
                foreach (var question in questions)
                {
                    writer.WriteStartObject();
                    writer.WriteString("header", question.Header);
                    writer.WriteString("text", question.Text);
                    writer.WriteBoolean("multiple", question.Multiple);
                    writer.WriteBoolean("custom", question.Custom);
                    writer.WriteStartArray("options");
                    foreach (var option in question.Options)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("label", option.Label);
                        writer.WriteString("description", option.Description);
                        writer.WriteEndObject();
                    }

                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            },
            answer => ParseQuestionAnswer(questions, answer),
            new QuestionResponse([], true),
            cancellationToken);
    }

    public async Task<string?> ChooseAsync(
        IReadOnlyList<SessionSummary> sessions,
        string? currentId,
        bool listWorkspace,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        var ids = sessions.Select(static session => session.Id).ToHashSet(StringComparer.Ordinal);
        return await OpenAsync<string?>(
            InteractionKind.ChooseSession,
            writer =>
            {
                WireWriter.Optional(writer, "currentId", currentId);
                writer.WriteBoolean("listWorkspace", listWorkspace);
                writer.WriteStartArray("sessions");
                foreach (var session in sessions)
                {
                    WireWriter.SessionSummary(writer, session);
                }

                writer.WriteEndArray();
            },
            answer => Choose(answer, "sessionId", ids),
            null,
            cancellationToken);
    }

    public async Task<string?> ChooseChildAsync(
        IReadOnlyList<ChildSessionDocument> children,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(children);
        var ids = children.Select(static child => child.Id).ToHashSet(StringComparer.Ordinal);
        return await OpenAsync<string?>(
            InteractionKind.ChooseChild,
            writer =>
            {
                writer.WriteStartArray("children");
                foreach (var child in children)
                {
                    WireWriter.Child(writer, child, full: false);
                }

                writer.WriteEndArray();
            },
            answer => Choose(answer, "childId", ids),
            null,
            cancellationToken);
    }

    public ValueTask<bool> ConfirmAsync(
        WorkspaceTrustRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return OpenAsync(
            InteractionKind.Trust,
            writer =>
            {
                writer.WriteString("workspace", request.Workspace);
                writer.WriteString("trustRoot", request.TrustRoot);
                writer.WriteBoolean("coversRepository", request.CoversRepository);
            },
            answer => answer.ValueKind == JsonValueKind.Object
                && answer.TryGetProperty("trust", out var trust)
                && trust.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? (true, trust.GetBoolean(), string.Empty)
                    : (false, false, "trust must be true or false."),
            false,
            cancellationToken);
    }

    private async ValueTask<T> OpenAsync<T>(
        string kind,
        Action<Utf8JsonWriter> payload,
        Func<JsonElement, (bool Valid, T Value, string Error)> parse,
        T cancelled,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var origin = _origin();
        var target = origin is not null && _connected(origin) ? origin : null;
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        var interaction = new Interaction(
            id,
            kind,
            target,
            payload,
            answer =>
            {
                var (valid, value, error) = parse(answer);
                if (valid)
                {
                    completion.TrySetResult(value);
                }

                return (valid, error);
            },
            () => completion.TrySetCanceled(cancellationToken.IsCancellationRequested ? cancellationToken : CancellationToken.None));
        _pending[id] = interaction;
        PublishOpened(interaction);
        await using var registration = cancellationToken.Register(() => Cancel(interaction));
        try
        {
            return await completion.Task;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Shutdown cancelled the question; the engine treats it as declined.
            return cancelled;
        }
    }

    private void Cancel(Interaction interaction)
    {
        if (!interaction.TryCancel())
        {
            return;
        }

        _pending.TryRemove(interaction.Id, out _);
        _hub.Publish("interaction.cancelled", null, writer =>
        {
            writer.WriteString("id", interaction.Id);
            writer.WriteString("kind", interaction.Kind);
        });
    }

    private void PublishOpened(Interaction interaction) =>
        _hub.Publish("interaction.opened", interaction.Target, writer =>
        {
            writer.WritePropertyName("interaction");
            interaction.Write(writer);
        });

    private static (bool, QuestionResponse, string) ParseQuestionAnswer(
        IReadOnlyList<UserQuestion> questions,
        JsonElement answer)
    {
        var rejected = new QuestionResponse([], true);
        if (answer.ValueKind != JsonValueKind.Object)
        {
            return (false, rejected, "The answer must be an object.");
        }

        if (answer.TryGetProperty("dismissed", out var dismissed) && dismissed.ValueKind == JsonValueKind.True)
        {
            return (true, rejected, string.Empty);
        }

        if (!answer.TryGetProperty("answers", out var answers)
            || answers.ValueKind != JsonValueKind.Array
            || answers.GetArrayLength() != questions.Count)
        {
            return (false, rejected, $"answers must be an array of {questions.Count} label arrays, or pass dismissed: true.");
        }

        var parsed = new List<IReadOnlyList<string>>(questions.Count);
        var index = 0;
        foreach (var labels in answers.EnumerateArray())
        {
            var question = questions[index];
            if (labels.ValueKind != JsonValueKind.Array)
            {
                return (false, rejected, $"Answer {index + 1} must be an array of labels.");
            }

            var values = new List<string>();
            foreach (var label in labels.EnumerateArray())
            {
                if (label.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(label.GetString()))
                {
                    return (false, rejected, $"Answer {index + 1} contains an empty label.");
                }

                var text = label.GetString()!.Trim();
                if (!question.Custom && question.Options.All(option => !string.Equals(option.Label, text, StringComparison.Ordinal)))
                {
                    return (false, rejected, $"Answer {index + 1} must use one of the offered labels.");
                }

                values.Add(text);
            }

            if (values.Count == 0 || (!question.Multiple && values.Count > 1))
            {
                return (false, rejected, question.Multiple
                    ? $"Answer {index + 1} needs at least one label."
                    : $"Answer {index + 1} needs exactly one label.");
            }

            parsed.Add(values);
            index++;
        }

        return (true, new QuestionResponse(parsed, false), string.Empty);
    }

    private static (bool, string?, string) Choose(JsonElement answer, string property, IReadOnlySet<string> ids)
    {
        if (answer.ValueKind != JsonValueKind.Object || !answer.TryGetProperty(property, out var value))
        {
            return (false, null, $"{property} is required; pass null to decline.");
        }

        if (value.ValueKind == JsonValueKind.Null)
        {
            return (true, null, string.Empty);
        }

        var id = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        return id is not null && ids.Contains(id)
            ? (true, id, string.Empty)
            : (false, null, $"{property} must be one of the offered ids.");
    }

    private static string? Text(JsonElement answer, string property) =>
        answer.ValueKind == JsonValueKind.Object
        && answer.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim().ToLowerInvariant()
            : null;
}
