using System.Text.Json;

using Crystal;
using Crystal.Chat;

using CrystalHarness.Prompts;

namespace CrystalHarness.Approvals;

/// <summary>
/// Asks another chat model whether a tool call is safe and on-request.
/// The user request must be attached; otherwise the reviewer does not run.
/// </summary>
public sealed class ModelApprovalReviewer : IApprovalReviewer
{
    private readonly IChatClient _client;

    public ModelApprovalReviewer(IChatClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    public async ValueTask<ApprovalReviewVerdict> ReviewAsync(
        ApprovalReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.UserRequest))
        {
            return ApprovalReviewVerdict.AskUser(
                "No user request is available to review against.");
        }

        ChatResponse response;
        try
        {
            response = await _client.CompleteAsync(
                new ChatRequest(
                [
                    new ChatMessage(ChatRole.System, ApprovalReviewPrompt.SystemText),
                    new ChatMessage(ChatRole.User, ApprovalReviewPrompt.UserText(request))
                ]),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return ApprovalReviewVerdict.AskUser(
                "The approval reviewer failed: " + exception.Message);
        }

        var text = ReadAssistantText(response);
        if (!TryParse(text, out var verdict))
        {
            return ApprovalReviewVerdict.AskUser(
                "The approval reviewer did not return a usable assessment.");
        }

        return verdict;
    }

    private static string ReadAssistantText(ChatResponse response)
    {
        var candidate = response.Candidates[0];
        foreach (var item in candidate.Items)
        {
            if (item is ChatMessage message
                && message.Role == ChatRole.Assistant
                && !string.IsNullOrWhiteSpace(message.Text))
            {
                return message.Text;
            }
        }

        return string.Empty;
    }

    private static bool TryParse(string text, out ApprovalReviewVerdict verdict)
    {
        verdict = ApprovalReviewVerdict.AskUser("Unparsed reviewer output.");
        if (!TryReadObject(text, out var document))
        {
            return false;
        }

        using (document)
        {
            if (!TryReadString(document.RootElement, ["outcome", "decision"], out var outcome)
                || !TryReadString(document.RootElement, ["risk_level", "risk"], out var riskText)
                || !TryReadString(
                    document.RootElement,
                    ["user_authorization", "authorization", "authority"],
                    out var authorizationText)
                || !TryReadString(document.RootElement, ["rationale", "reason"], out var rationale)
                || !ReviewRiskLevel.TryParse(riskText, out var riskLevel)
                || !ReviewAuthorization.TryParse(authorizationText, out var authorization))
            {
                return false;
            }

            var normalized = outcome.Trim().ToLowerInvariant();
            if (normalized is "ask_user")
            {
                normalized = "ask";
            }

            if (normalized is not ("allow" or "deny" or "ask"))
            {
                return false;
            }

            verdict = new ApprovalReviewVerdict(
                normalized,
                riskLevel,
                authorization,
                rationale);
            return true;
        }
    }

    private static bool TryReadString(
        JsonElement root,
        string[] names,
        out string value)
    {
        value = string.Empty;
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var property)
                || property.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var text = property.GetString();
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            value = text.Trim();
            return true;
        }

        return false;
    }

    private static bool TryReadObject(string text, out JsonDocument document)
    {
        var json = ExtractJsonObject(text);
        try
        {
            document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                return true;
            }

            document.Dispose();
            document = null!;
            return false;
        }
        catch (JsonException)
        {
            document = null!;
            return false;
        }
    }

    private static string ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return text;
        }

        return text[start..(end + 1)];
    }
}
