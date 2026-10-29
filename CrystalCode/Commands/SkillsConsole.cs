using System.Text.Json;

using CrystalCode.Display.Paint;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Skills;

using Spectre.Console;

namespace CrystalCode.Commands;

/// <summary>
/// Renders discovered skills. The default is a Spectre table.
/// <c>--format text</c> prints tab-separated plain text.
/// </summary>
public static class SkillsConsole
{
    private const string SkillsOff = "Skills are off. They will not load into a session until skills are turned on.";

    public static int Run(SkillsSettings settings, string? name)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!ExtensionConsole.TryParseFormat(settings.Format, out var plain, out var formatError))
        {
            Fail(plain: false, formatError);
            return 1;
        }

        var home = CrystalHome.Resolve(settings.Home);
        var workspace = string.IsNullOrWhiteSpace(settings.Workspace)
            ? Path.GetFullPath(Environment.CurrentDirectory)
            : Path.GetFullPath(settings.Workspace);
        bool enabled;
        try
        {
            enabled = !File.Exists(home.ConfigPath) || new DiskSettingsStore(home).Load().Skills;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException)
        {
            Fail(plain, "config.json could not be read.");
            return 1;
        }

        var catalog = SkillDiscovery.Create(home).Collect(workspace);
        if (name is null)
        {
            WriteList(plain, catalog.Items, workspace);
            if (!enabled)
            {
                Line(plain, SkillsOff);
            }

            return 0;
        }

        var skill = catalog.Find(name);
        if (skill is null)
        {
            Fail(plain, $"Skill '{name.Trim()}' was not found.");
            return 1;
        }

        WriteOne(plain, skill, workspace);
        if (!enabled)
        {
            Line(plain, SkillsOff);
        }

        return 0;
    }

    private static void WriteList(bool plain, IReadOnlyList<SkillInfo> skills, string workspace)
    {
        if (plain)
        {
            foreach (var skill in skills)
            {
                Console.Out.WriteLine(
                    $"{skill.Name}\t{Source(skill, workspace)}\t{Flatten(skill.Description)}\t{skill.Location}");
            }

            return;
        }

        if (skills.Count == 0)
        {
            AnsiConsole.MarkupLine($"[{Theme.Muted}]No skills.[/]");
            return;
        }

        var table = CreateTable("Name", "Source", "Description", "Path");
        foreach (var skill in skills)
        {
            table.AddRow(
                Markup.Escape(skill.Name),
                Source(skill, workspace),
                Markup.Escape(Flatten(skill.Description)),
                Markup.Escape(skill.Location));
        }

        AnsiConsole.Write(table);
    }

    private static void WriteOne(bool plain, SkillInfo skill, string workspace)
    {
        if (plain)
        {
            Console.Out.WriteLine($"Name: {skill.Name}");
            Console.Out.WriteLine($"Source: {Source(skill, workspace)}");
            Console.Out.WriteLine($"Description: {Flatten(skill.Description)}");
            Console.Out.WriteLine($"Path: {skill.Location}");
            return;
        }

        var table = CreateTable("Field", "Value");
        Add(table, "Name", skill.Name);
        Add(table, "Source", Source(skill, workspace));
        Add(table, "Description", Flatten(skill.Description));
        Add(table, "Path", skill.Location);
        AnsiConsole.Write(table);
    }

    // Matches the /skills listing: a skill under the workspace root is Project.
    private static string Source(SkillInfo skill, string workspace)
    {
        var root = workspace.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return skill.Location.StartsWith(root, StringComparison.Ordinal) ? "Project" : "Home";
    }

    private static string Flatten(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static Table CreateTable(params string[] columns)
    {
        var table = new Table { Border = TableBorder.Simple };
        foreach (var column in columns)
        {
            table.AddColumn(new TableColumn($"[{Theme.Chrome}]{Markup.Escape(column)}[/]"));
        }

        return table;
    }

    private static void Add(Table table, string field, string value) =>
        table.AddRow($"[{Theme.Chrome}]{Markup.Escape(field)}[/]", Markup.Escape(value));

    private static void Line(bool plain, string text)
    {
        if (plain)
        {
            Console.Out.WriteLine(text);
            return;
        }

        AnsiConsole.MarkupLine($"[{Theme.Muted}]{Markup.Escape(text)}[/]");
    }

    private static void Fail(bool plain, string text)
    {
        if (plain)
        {
            Console.Error.WriteLine(text);
            return;
        }

        AnsiConsole.MarkupLine($"[{Theme.Fail}]{Markup.Escape(text)}[/]");
    }
}
