using Crystal.Tools;

using CrystalHarness.Skills;

namespace CrystalHarness.Tools;

/// <summary>
/// Loads one discovered skill into the conversation.
/// </summary>
public sealed class SkillTool : ITool
{
    internal const string ToolName = "skill";

    private const string ToolDescription =
        "Load a specialized skill when the task at hand matches one of the skills listed in the system prompt. "
        + "Use this tool to inject the skill's instructions and resources into the current conversation. "
        + "The output may contain detailed workflow guidance as well as references to scripts, files, "
        + "and other resources in the same directory as the skill. "
        + "The skill name must match one of the skills listed in the system prompt.";

    private readonly SkillCatalog _catalog;

    public SkillTool(SkillCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
        Definition = new ToolDefinition(
            ToolName,
            ToolSchema.Parse(
                """
                {
                  "type": "object",
                  "properties": {
                    "name": {
                      "type": "string",
                      "description": "The name of the skill from available_skills."
                    }
                  },
                  "required": ["name"]
                }
                """),
            ToolDescription);
    }

    public ToolDefinition Definition { get; }

    public ValueTask<ToolOutput> InvokeAsync(
        ToolCall call,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(call);

        if (!ToolArguments.TryReadRequiredString(call.Arguments, "name", out var name))
        {
            return ValueTask.FromResult(
                new ToolOutput(
                    "Arguments must include name.",
                    ToolResultStatus.Failure));
        }

        var skill = _catalog.Find(name);
        if (skill is null)
        {
            var available = _catalog.Count == 0
                ? "none"
                : string.Join(", ", _catalog.Items.Select(item => item.Name));
            return ValueTask.FromResult(
                new ToolOutput(
                    $"Skill '{name}' was not found. Available skills: {available}.",
                    ToolResultStatus.Failure));
        }

        var files = ListFiles(skill.Directory, cancellationToken);
        return ValueTask.FromResult(new ToolOutput(ToolOutputText.Truncate(Format(skill, files))));
    }

    private static IReadOnlyList<string> ListFiles(string directory, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories);
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }

        var listed = new List<string>();
        foreach (var path in files.OrderBy(item => item, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(Path.GetFileName(path), SkillFiles.FileName, StringComparison.Ordinal))
            {
                continue;
            }

            listed.Add(Path.GetFullPath(path));
            if (listed.Count >= SkillFiles.MaximumListedFiles)
            {
                break;
            }
        }

        return listed;
    }

    private static string Format(SkillInfo skill, IReadOnlyList<string> files)
    {
        var lines = new List<string>
        {
            $"<skill_content name=\"{skill.Name}\">",
            $"# Skill: {skill.Name}",
            string.Empty,
            skill.Content,
            string.Empty,
            $"Base directory for this skill: {skill.Directory}",
            "Relative paths in this skill (e.g., scripts/, reference/) are relative to this base directory.",
            "Note: file list is sampled.",
            string.Empty,
            "<skill_files>"
        };
        foreach (var file in files)
        {
            lines.Add($"<file>{file}</file>");
        }

        lines.Add("</skill_files>");
        lines.Add("</skill_content>");
        return string.Join('\n', lines);
    }
}
