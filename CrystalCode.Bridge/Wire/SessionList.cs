using System.Globalization;
using System.Text;
using System.Text.Json;

using CrystalCode.Bridge.Transport;
using CrystalCode.Engine.Home;

namespace CrystalCode.Bridge.Wire;

/// <summary>
/// <c>session/list</c>: saved sessions newest first, in pages of
/// <see cref="PageSize"/>. The cursor is the base64 of the next offset; any
/// other cursor is <c>-32602</c>.
/// </summary>
internal static class SessionList
{
    public const int PageSize = 50;

    public static void Write(
        Utf8JsonWriter writer,
        IReadOnlyList<SessionSummary> sessions,
        string? cursor,
        Func<SessionSummary, IReadOnlyList<string>> outsideRoots)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(sessions);
        var start = Offset(cursor);
        var page = sessions.Skip(start).Take(PageSize).ToArray();
        writer.WriteStartObject();
        writer.WriteStartArray("sessions");
        foreach (var session in page)
        {
            writer.WriteStartObject();
            writer.WriteString("sessionId", session.Id);
            writer.WriteString("cwd", session.Workspace);
            writer.WriteString("title", session.Title);
            if (session.UpdatedUtc is { } updated)
            {
                writer.WriteString("updatedAt", updated.ToString("O", CultureInfo.InvariantCulture));
            }

            var roots = outsideRoots(session);
            if (roots.Count > 0)
            {
                writer.WriteStartArray("additionalDirectories");
                foreach (var root in roots)
                {
                    writer.WriteStringValue(root);
                }

                writer.WriteEndArray();
            }

            writer.WriteStartObject("_meta");
            writer.WriteStartObject("crystal");
            JsonText.Optional(writer, "mode", session.Mode);
            writer.WriteBoolean("planMode", session.PlanMode);
            writer.WriteNumber("userTurns", session.UserTurns);
            if (session.CreatedUtc is { } created)
            {
                writer.WriteString("createdAt", created.ToString("O", CultureInfo.InvariantCulture));
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        if (start + page.Length < sessions.Count)
        {
            writer.WriteString("nextCursor", Cursor(start + page.Length));
        }

        writer.WriteEndObject();
    }

    private static string Cursor(int offset) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes("o" + offset.ToString(CultureInfo.InvariantCulture)));

    private static int Offset(string? cursor)
    {
        if (cursor is null)
        {
            return 0;
        }

        try
        {
            var text = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            if (text.Length > 1
                && text[0] == 'o'
                && int.TryParse(text.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var offset))
            {
                return offset;
            }
        }
        catch (FormatException)
        {
        }

        throw JsonRpcException.InvalidParams("cursor is not a cursor this agent returned.");
    }
}
