using System.Text.Json;

using CrystalCode.Bridge.Transport;
using CrystalCode.Engine.Events;

namespace CrystalCode.Bridge.Wire;

/// <summary>
/// ACP <c>configOptions</c> and the legacy <c>modes</c> field from the
/// engine's option catalog. A switch is a boolean option only for a client
/// that advertised <c>session.configOptions.boolean</c>, and a two-value
/// select otherwise. A value without a command is listed only when it is the
/// current one, so a client never offers a choice that cannot be made.
/// </summary>
internal static class ConfigWire
{
    public static void Options(Utf8JsonWriter writer, string name, IReadOnlyList<SessionOption> options, bool booleans)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(options);
        writer.WriteStartArray(name);
        foreach (var option in options)
        {
            Option(writer, option, booleans);
        }

        writer.WriteEndArray();
    }

    /// <summary>The legacy mode state mirroring the <c>mode</c> option.</summary>
    public static void Modes(Utf8JsonWriter writer, IReadOnlyList<SessionOption> options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (options.FirstOrDefault(static option => option.Id == "mode") is not { } mode)
        {
            return;
        }

        writer.WriteStartObject("modes");
        writer.WriteString("currentModeId", mode.Current);
        writer.WriteStartArray("availableModes");
        foreach (var value in mode.Values)
        {
            writer.WriteStartObject();
            writer.WriteString("id", value.Value);
            writer.WriteString("name", value.Name);
            JsonText.Optional(writer, "description", value.Description);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    /// <summary>
    /// The slash command that sets <paramref name="value"/>, or null when the
    /// option does not offer it.
    /// </summary>
    public static string? Command(SessionOption option, JsonElement value)
    {
        ArgumentNullException.ThrowIfNull(option);
        var text = value.ValueKind switch
        {
            JsonValueKind.True when option.IsSwitch => "on",
            JsonValueKind.False when option.IsSwitch => "off",
            JsonValueKind.String => value.GetString(),
            _ => null
        };
        var match = option.Values.FirstOrDefault(entry => string.Equals(entry.Value, text, StringComparison.Ordinal));
        return match is null || match.Command.Length == 0 ? null : match.Command;
    }

    private static void Option(Utf8JsonWriter writer, SessionOption option, bool booleans)
    {
        writer.WriteStartObject();
        writer.WriteString("id", option.Id);
        writer.WriteString("name", option.Name);
        writer.WriteString("category", option.Category);
        JsonText.Optional(writer, "description", option.Description);
        if (option.IsSwitch && booleans)
        {
            writer.WriteString("type", "boolean");
            writer.WriteBoolean("currentValue", option.Current == "on");
            writer.WriteEndObject();
            return;
        }

        writer.WriteString("type", "select");
        writer.WriteString("currentValue", option.Current);
        var values = option.Values
            .Where(value => value.Command.Length > 0 || value.Value == option.Current)
            .ToArray();
        writer.WriteStartArray("options");
        if (values.Any(static value => value.Group is not null))
        {
            foreach (var group in values.GroupBy(static value => value.Group ?? string.Empty))
            {
                writer.WriteStartObject();
                writer.WriteString("group", group.Key);
                writer.WriteString("name", group.Key.Length == 0 ? option.Name : group.Key);
                writer.WriteStartArray("options");
                foreach (var value in group)
                {
                    Value(writer, value);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }
        }
        else
        {
            foreach (var value in values)
            {
                Value(writer, value);
            }
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void Value(Utf8JsonWriter writer, SessionOptionValue value)
    {
        writer.WriteStartObject();
        writer.WriteString("value", value.Value);
        writer.WriteString("name", value.Name);
        JsonText.Optional(writer, "description", value.Description);
        writer.WriteEndObject();
    }
}
