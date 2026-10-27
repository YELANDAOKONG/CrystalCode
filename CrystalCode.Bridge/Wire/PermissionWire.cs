using System.Text.Json;

using Crystal.Tools;

using CrystalCode.Engine.Approvals;

namespace CrystalCode.Bridge.Wire;

/// <summary>
/// <c>session/request_permission</c> for one approval card. Session and
/// Always options are offered only when the classification allows a
/// reusable grant; any other answer, a cancelled outcome, or an option that
/// was not offered is Deny. Bridge never answers a permission itself.
/// </summary>
internal static class PermissionWire
{
    public const string Once = "once";

    public const string ForSession = "session";

    public const string Always = "always";

    public const string Deny = "deny";

    public static void Request(
        Utf8JsonWriter writer,
        string sessionId,
        ToolCall call,
        ToolClassification classification,
        ApprovalReviewVerdict? review,
        ApprovalReviewVerdict? extendedReview,
        string? workspaceRoot)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(classification);
        writer.WriteStartObject();
        writer.WriteString("sessionId", sessionId);
        writer.WriteStartObject("toolCall");
        writer.WriteString("toolCallId", call.CallId);
        writer.WriteString("name", call.Name);
        writer.WriteString("status", "pending");
        ToolCallWire.Describe(writer, call, workspaceRoot);
        writer.WriteEndObject();
        writer.WriteStartArray("options");
        Option(writer, Once, "Allow once", "allow_once");
        if (classification.AllowsReusableGrant)
        {
            Option(writer, ForSession, "Allow for this session", "allow_always");
            Option(writer, Always, "Always allow", "allow_always");
        }

        Option(writer, Deny, "Deny", "reject_once");
        writer.WriteEndArray();
        writer.WriteStartObject("_meta");
        writer.WriteStartObject("crystal");
        writer.WriteString("risk", classification.Risk.Value);
        writer.WriteString("authority", classification.Authority.Value);
        writer.WriteString("summary", classification.Summary);
        writer.WriteBoolean("requirePrompt", classification.RequirePrompt);
        Verdict(writer, "review", review);
        Verdict(writer, "extendedReview", extendedReview);
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    public static ApprovalChoice Choice(JsonElement result, bool reusable)
    {
        if (result.ValueKind != JsonValueKind.Object
            || !result.TryGetProperty("outcome", out var outcome)
            || outcome.ValueKind != JsonValueKind.Object
            || !outcome.TryGetProperty("outcome", out var kind)
            || kind.ValueKind != JsonValueKind.String
            || kind.GetString() != "selected"
            || !outcome.TryGetProperty("optionId", out var option)
            || option.ValueKind != JsonValueKind.String)
        {
            return ApprovalChoice.Deny;
        }

        return option.GetString() switch
        {
            Once => ApprovalChoice.AllowOnce,
            ForSession when reusable => ApprovalChoice.AllowSession,
            Always when reusable => ApprovalChoice.AllowPersistent,
            _ => ApprovalChoice.Deny
        };
    }

    private static void Option(Utf8JsonWriter writer, string id, string name, string kind)
    {
        writer.WriteStartObject();
        writer.WriteString("optionId", id);
        writer.WriteString("name", name);
        writer.WriteString("kind", kind);
        writer.WriteEndObject();
    }

    private static void Verdict(Utf8JsonWriter writer, string name, ApprovalReviewVerdict? verdict)
    {
        if (verdict is null)
        {
            return;
        }

        writer.WriteStartObject(name);
        writer.WriteString("outcome", verdict.Outcome);
        writer.WriteString("riskLevel", verdict.RiskLevel.Value);
        writer.WriteString("userAuthorization", verdict.UserAuthorization.Value);
        writer.WriteString("rationale", verdict.Rationale);
        writer.WriteEndObject();
    }
}
