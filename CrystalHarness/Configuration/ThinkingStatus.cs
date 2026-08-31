namespace CrystalHarness.Configuration;

/// <summary>
/// Status-bar thinking text. Empty when the model does not support thinking.
/// </summary>
public static class ThinkingStatus
{
    public static string For(ModelSettings model, ThinkingSelection selection)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(selection);
        if (!model.Thinking)
        {
            return string.Empty;
        }

        if (selection == ThinkingSelection.Off)
        {
            return "Think Off";
        }

        if (selection == ThinkingSelection.Default || !model.AllowsEffort(selection.Value))
        {
            return "Think Default";
        }

        return "Think " + ThinkingLabel.For(selection);
    }
}
