using System.Net;

namespace CrystalCode.Skills;

/// <summary>
/// Host-owned available-skill text. Not an overlay file.
/// </summary>
public static class SkillGuidance
{
    public static string Render(SkillCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (catalog.Count == 0)
        {
            return
                """
                Skills provide specialized instructions and workflows for specific tasks.
                Use the skill tool to load a skill when a task matches its description.
                No skills are currently available.
                """;
        }

        var lines = new List<string>
        {
            "Skills provide specialized instructions and workflows for specific tasks.",
            "Use the skill tool to load a skill when a task matches its description.",
            "<available_skills>"
        };
        foreach (var skill in catalog.Items)
        {
            lines.Add("  <skill>");
            lines.Add($"    <name>{Escape(skill.Name)}</name>");
            lines.Add($"    <description>{Escape(skill.Description)}</description>");
            lines.Add("  </skill>");
        }

        lines.Add("</available_skills>");
        return string.Join('\n', lines);
    }

    private static string Escape(string value) => WebUtility.HtmlEncode(value);
}
