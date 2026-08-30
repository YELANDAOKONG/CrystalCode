using System.Text.Json;

using Crystal;
using Crystal.Chat;

using CrystalHarness.Prompts;

namespace CrystalHarness.Approvals;

/// <summary>
/// Asks another chat model whether a tool call is safe and on-request.
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
                "The approval reviewer did not return a usable decision.");
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
            if (!document.RootElement.TryGetProperty("decision", out var decision)
                || decision.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var reason = "No reason was provided.";
            if (document.RootElement.TryGetProperty("reason", out var reasonElement)
                && reasonElement.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(reasonElement.GetString()))
            {
                reason = reasonElement.GetString()!;
            }

            var value = decision.GetString() ?? string.Empty;
            if (value.Equals("allow", StringComparison.OrdinalIgnoreCase))
            {
                verdict = ApprovalReviewVerdict.Allow(reason);
                return true;
            }

            if (value.Equals("deny", StringComparison.OrdinalIgnoreCase))
            {
                verdict = ApprovalReviewVerdict.Deny(reason);
                return true;
            }

            if (value.Equals("ask", StringComparison.OrdinalIgnoreCase)
                || value.Equals("ask_user", StringComparison.OrdinalIgnoreCase))
            {
                verdict = ApprovalReviewVerdict.AskUser(reason);
                return true;
            }

            return false;
        }
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
