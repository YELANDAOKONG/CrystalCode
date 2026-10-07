using System.Text;
using CrystalCode.Engine.Prompts;

namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Writes built-in prompt templates for operator customization.
/// </summary>
public static class PromptTemplateExport
{
    public static IReadOnlyList<string> Write(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);
        var files = new (string Name, string Text)[]
        {
            ("work.md", WorkPrompt.Text),
            ("plan.md", PlanPrompt.Text),
            ("review.md", ApprovalReviewPrompt.SystemText),
            ("topic.md", TopicNamingPrompt.Text),
            ("review.user.md", ApprovalReviewPrompt.UserTemplate),
            ("compaction.system.md", CompactionPrompt.SystemText),
            ("compaction.user.md", CompactionPrompt.UserTemplate),
            ("placeholders.md", RenderPlaceholderGuide())
        };

        var written = new List<string>();
        foreach (var (name, text) in files)
        {
            var path = Path.Combine(directory, name);
            File.WriteAllText(path, text, Encoding.UTF8);
            written.Add(path);
        }

        return written;
    }

    private static string RenderPlaceholderGuide() =>
        """
        # Crystal Code prompt placeholders

        Syntax: {{name}} (case-insensitive). Unknown names are left unchanged.

        ## Session

        - {{env}}
        - {{skills}}
        - {{instructions}}
        - {{instructions_section}}
        - {{workspace}}
        - {{is_git_repo}}
        - {{git_root}}
        - {{platform}}
        - {{os}}
        - {{architecture}}
        - {{date}}
        - {{time}}
        - {{provider}}
        - {{model}}
        - {{model_line}}
        - {{mode}}
        - {{product_name}}
        - {{session_id}}
        - {{approval}}

        ## Review user

        - {{conversation}}
        - {{tool_name}}
        - {{tool_arguments}}
        - {{host_risk}}
        - {{host_authority}}
        - {{classification_summary}}

        ## Compaction user

        - {{conversation}}
        - {{prior_summary_section}}
        - {{summary_task}}
        - {{output_template}}
        - {{todos_section}}
        """;
}
