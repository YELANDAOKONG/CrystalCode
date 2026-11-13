using System.Text.Json;

namespace CrystalCode.Engine.Sessions.Pursue;

/// <summary>
/// Reads the checker's single JSON object. Anything that does not match the
/// contract is invalid; there is no repair request and no lenient reading.
/// </summary>
public static class PursueVerdictParser
{
    /// <param name="text">The model reply.</param>
    /// <param name="validEntries">The entry numbers the checker was shown.</param>
    public static bool TryParse(
        string text,
        IReadOnlySet<int> validEntries,
        out PursueVerdict verdict,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(validEntries);
        verdict = null!;
        var body = StripFence(text.Trim());
        if (body.Length == 0)
        {
            error = "The checker returned no text.";
            return false;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            error = "The checker reply is not a JSON object.";
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "The checker reply is not a JSON object.";
                return false;
            }

            if (!TryKind(root, out var kind))
            {
                error = "The checker verdict must be done, continue, blocked, or wait.";
                return false;
            }

            var reason = ReadString(root, "reason");
            if (reason.Length == 0)
            {
                error = "The checker gave no reason.";
                return false;
            }

            var nextStep = ReadString(root, "nextStep");
            if (!TryNumbers(root, "evidence", out var evidence)
                || !TryStrings(root, "waitFor", out var waitFor))
            {
                error = "The checker evidence and waitFor fields must be arrays.";
                return false;
            }

            if (evidence.Any(number => !validEntries.Contains(number)))
            {
                error = "The checker cited an entry that was not shown.";
                return false;
            }

            switch (kind)
            {
                case PursueVerdictKind.Done when evidence.Count == 0:
                    error = "A done verdict must cite at least one entry.";
                    return false;
                case PursueVerdictKind.Continue when nextStep.Length == 0:
                    error = "A continue verdict must give a next step.";
                    return false;
                case PursueVerdictKind.Wait when waitFor.Count == 0:
                    error = "A wait verdict must list background task ids.";
                    return false;
                default:
                    break;
            }

            verdict = new PursueVerdict(kind, reason, nextStep, evidence, waitFor);
            error = string.Empty;
            return true;
        }
    }

    private static bool TryKind(JsonElement root, out PursueVerdictKind kind)
    {
        kind = PursueVerdictKind.Continue;
        if (!root.TryGetProperty("verdict", out var value) || value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        switch (value.GetString())
        {
            case "done":
                kind = PursueVerdictKind.Done;
                return true;
            case "continue":
                kind = PursueVerdictKind.Continue;
                return true;
            case "blocked":
                kind = PursueVerdictKind.Blocked;
                return true;
            case "wait":
                kind = PursueVerdictKind.Wait;
                return true;
            default:
                return false;
        }
    }

    private static string ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? (value.GetString() ?? string.Empty).Trim()
            : string.Empty;

    private static bool TryNumbers(JsonElement root, string name, out IReadOnlyList<int> numbers)
    {
        var list = new List<int>();
        numbers = list;
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number || !item.TryGetInt32(out var number))
            {
                return false;
            }

            list.Add(number);
        }

        return true;
    }

    private static bool TryStrings(JsonElement root, string name, out IReadOnlyList<string> strings)
    {
        var list = new List<string>();
        strings = list;
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
            {
                return false;
            }

            list.Add(item.GetString()!.Trim());
        }

        return true;
    }

    private static string StripFence(string text)
    {
        if (!text.StartsWith("```", StringComparison.Ordinal) || !text.EndsWith("```", StringComparison.Ordinal) || text.Length < 6)
        {
            return text;
        }

        var inner = text[3..^3];
        var newline = inner.IndexOf('\n');
        return newline < 0 ? string.Empty : inner[(newline + 1)..].Trim();
    }
}
