using System.Text;

namespace CrystalCode.Engine.Tools;

internal static class BackgroundTaskText
{
    public static string Snapshot(BackgroundTaskSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var text = new StringBuilder();
        ToolOutputText.AppendLine(text, $"task_id: {snapshot.TaskId}");
        ToolOutputText.AppendLine(text, $"status: {snapshot.Status}");
        ToolOutputText.AppendLine(text, $"command: {snapshot.Command}");
        ToolOutputText.AppendLine(text, $"working_directory: {snapshot.WorkingDirectory}");
        ToolOutputText.AppendLine(text, $"started_utc: {snapshot.StartedUtc:O}");
        if (snapshot.FinishedUtc is DateTimeOffset finishedUtc)
        {
            ToolOutputText.AppendLine(text, $"finished_utc: {finishedUtc:O}");
        }

        if (snapshot.ExitCode is int exitCode)
        {
            ToolOutputText.AppendLine(text, $"exit_code: {exitCode}");
        }

        ToolOutputText.AppendLine(text, $"log_offset: {snapshot.LogStartOffset}");
        ToolOutputText.AppendLine(text, $"log_end: {snapshot.LogEndOffset}");
        return text.ToString().TrimEnd(ToolOutputText.LineSeparator);
    }
}
