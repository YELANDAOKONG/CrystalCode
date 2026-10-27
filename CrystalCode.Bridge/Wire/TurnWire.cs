using System.Text.Json;

using Crystal;

using CrystalCode.Engine.Events;
using CrystalCode.Engine.Sessions;

namespace CrystalCode.Bridge.Wire;

/// <summary>
/// The end of one prompt turn: the ACP stop reason for an engine stop
/// reason, and the turn counts and usage under <c>_meta.crystal</c>, written
/// with the same field names as the <c>crystal run</c> JSON log.
/// </summary>
internal static class TurnWire
{
    public const string EndTurn = "end_turn";

    public const string Cancelled = "cancelled";

    /// <summary>The ACP stop reason, or null for a stop that is an error.</summary>
    public static string? StopReason(TurnStopReason reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        return reason.Value switch
        {
            "completed" => EndTurn,
            "output_truncated" => "max_tokens",
            "model_call_limit_reached" or "tool_call_limit_reached" or "duration_limit_reached" => "max_turn_requests",
            "content_filtered" => "refusal",
            "interrupted" => Cancelled,
            _ => null
        };
    }

    /// <summary>Writes the fields of <c>_meta.crystal</c> for a finished turn.</summary>
    public static void Crystal(Utf8JsonWriter writer, TurnFinished finished)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(finished);
        writer.WriteString("stopReason", finished.Result.StopReason.Value);
        writer.WriteNumber("modelCalls", finished.Result.ModelCallCount);
        writer.WriteNumber("toolCalls", finished.Result.ToolCallCount);
        // Usage is omitted when a provider did not report every round.
        Usage(writer, "usage", finished.Result.AccumulatedUsage);
        Usage(writer, "lastRequestUsage", finished.Usage);
        Usage(writer, "cumulativeUsage", finished.CumulativeUsage);
        writer.WriteNumber("contextWindow", finished.ContextWindow);
    }

    /// <summary>The <c>session/prompt</c> result.</summary>
    public static void Response(Utf8JsonWriter writer, string stopReason, TurnFinished? finished)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStartObject();
        writer.WriteString("stopReason", stopReason);
        if (finished is not null)
        {
            writer.WriteStartObject("_meta");
            writer.WriteStartObject("crystal");
            Crystal(writer, finished);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    public static void Usage(Utf8JsonWriter writer, string name, TokenUsage? usage)
    {
        if (usage is null)
        {
            return;
        }

        writer.WriteStartObject(name);
        writer.WriteNumber("inputTokens", usage.InputTokenCount);
        writer.WriteNumber("outputTokens", usage.OutputTokenCount);
        if (usage.ReasoningTokenCount is long reasoning)
        {
            writer.WriteNumber("reasoningTokens", reasoning);
        }

        writer.WriteEndObject();
    }
}
