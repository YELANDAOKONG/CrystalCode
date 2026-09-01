namespace CrystalCode.Display.Paint;

/// <summary>
/// Checkbox colors for session todos. Marks match TodoList: space, ~, x, dash.
/// </summary>
public static class TodoMarks
{
    public static string? Color(char mark) =>
        mark switch
        {
            ' ' => Theme.Chrome,
            '~' => Theme.Accent,
            'x' => Theme.Ok,
            '-' => Theme.Muted,
            _ => null
        };
}
