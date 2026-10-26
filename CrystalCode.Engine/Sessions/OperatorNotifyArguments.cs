using CrystalCode.Engine.Configuration;

namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Validates <c>/notify</c> arguments. An empty argument only displays the setting.
/// </summary>
public static class OperatorNotifyArguments
{
    public const string Usage = "Notify command must be /notify, /notify on|off, or /notify test.";

    public enum Action
    {
        Show,
        On,
        Off,
        Test
    }

    public static bool TryParse(string? argument, out Action action, out string error)
    {
        action = Action.Show;
        error = string.Empty;
        var text = argument?.Trim() ?? string.Empty;
        switch (text.ToLowerInvariant())
        {
            case "":
                return true;
            case "on":
                action = Action.On;
                return true;
            case "off":
                action = Action.Off;
                return true;
            case "test":
                action = Action.Test;
                return true;
            default:
                error = Usage;
                return false;
        }
    }

    /// <summary>
    /// One line per field. The command is shown by executable name only,
    /// because its arguments are operator text that may hold a webhook URL.
    /// </summary>
    public static string Format(OperatorNotifySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var command = settings.HasCommand
            ? Path.GetFileName(settings.Command[0]) + (settings.Command.Count > 1
                ? $" ({settings.Command.Count - 1} arguments)"
                : string.Empty)
            : "not set";
        return "Operator notification  " + (settings.Enabled ? "on" : "off")
            + "\nCommand  " + command
            + "\nEvents  " + string.Join(", ", settings.Events.Select(static value => value.Value))
            + "\nDelay  " + settings.DelaySeconds + "s, timeout " + settings.TimeoutSeconds + "s";
    }
}
