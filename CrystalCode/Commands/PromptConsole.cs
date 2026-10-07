using CrystalCode.Display.Paint;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Prompts;

using Spectre.Console;

namespace CrystalCode.Commands;

/// <summary>
/// Renders prompt set and prompt attachment manifests. The default is a
/// Spectre table. <c>--format text</c> prints aligned plain text.
/// </summary>
public static class PromptConsole
{
    public static int Sets(PromptSetCommandSettings settings, string? directory, string verb)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!ExtensionConsole.TryParseFormat(settings.Format, out var plain, out var formatError))
        {
            Fail(plain: false, formatError);
            return 1;
        }

        var home = CrystalHome.Resolve(settings.Home);
        if (verb == "list")
        {
            WriteSets(plain, PromptSetInventory.List(home));
            return 0;
        }

        if (string.IsNullOrWhiteSpace(directory))
        {
            Fail(plain, "Directory is required.");
            return 1;
        }

        if (verb == "show")
        {
            return ShowSet(plain, home, directory);
        }

        var enabled = verb == "enable";
        var changed = enabled
            ? PromptSetInventory.TryEnable(home, directory, out var error)
            : PromptSetInventory.TryDisable(home, directory, out error);
        if (!changed)
        {
            Fail(plain, error);
            return 1;
        }

        return ShowSet(plain, home, directory);
    }

    public static int Attachments(PromptAttachmentCommandSettings settings, string? directory, string verb)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!ExtensionConsole.TryParseFormat(settings.Format, out var plain, out var formatError))
        {
            Fail(plain: false, formatError);
            return 1;
        }

        if (!ExtensionConsole.TryParseSource(settings.Source, out var sourceName, out var sourceError))
        {
            Fail(plain, sourceError);
            return 1;
        }

        var home = CrystalHome.Resolve(settings.Home);
        var workspace = string.IsNullOrWhiteSpace(settings.Workspace)
            ? Path.GetFullPath(Environment.CurrentDirectory)
            : Path.GetFullPath(settings.Workspace);
        if (verb == "list")
        {
            WriteAttachments(plain, PromptAttachmentInventory.List(home, workspace));
            return 0;
        }

        if (string.IsNullOrWhiteSpace(directory))
        {
            Fail(plain, "Directory is required.");
            return 1;
        }

        if (verb == "show")
        {
            return ShowAttachment(plain, home, workspace, directory, sourceName);
        }

        var enabled = verb == "enable";
        if (!PromptAttachmentInventory.TrySetEnabled(
                home,
                workspace,
                directory,
                sourceName,
                enabled,
                out var error))
        {
            Fail(plain, error);
            return 1;
        }

        return ShowAttachment(plain, home, workspace, directory, sourceName);
    }

    private static int ShowSet(bool plain, CrystalHome home, string directory)
    {
        var entry = Find(PromptSetInventory.List(home), directory, source: null);
        if (entry is null)
        {
            Fail(plain, "Prompt set '" + directory.Trim() + "' was not found.");
            return 1;
        }

        WriteSet(plain, entry);
        return 0;
    }

    private static int ShowAttachment(
        bool plain,
        CrystalHome home,
        string workspace,
        string directory,
        string? source)
    {
        var entry = Find(PromptAttachmentInventory.List(home, workspace), directory, source);
        if (entry is null)
        {
            var place = source switch
            {
                "home" => " in Home",
                "project" => " in Workspace",
                _ => string.Empty
            };
            Fail(plain, "Prompt attachment '" + directory.Trim() + "' was not found" + place + ".");
            return 1;
        }

        WriteAttachment(plain, entry);
        return 0;
    }

    private static PromptCatalogEntry? Find(
        IReadOnlyList<PromptCatalogEntry> entries,
        string directory,
        string? source)
    {
        var name = directory.Trim();
        PromptCatalogEntry? match = null;
        foreach (var entry in entries)
        {
            if (!string.Equals(entry.DirectoryName, name, StringComparison.Ordinal))
            {
                continue;
            }

            if (source == "home" && !string.Equals(entry.Source, "Home", StringComparison.Ordinal))
            {
                continue;
            }

            if (source == "project" && !string.Equals(entry.Source, "Workspace", StringComparison.Ordinal))
            {
                continue;
            }

            if (match is null
                || (string.Equals(entry.Source, "Workspace", StringComparison.Ordinal)
                    && !string.Equals(match.Source, "Workspace", StringComparison.Ordinal)))
            {
                match = entry;
            }
        }

        return match;
    }

    private static void WriteSets(bool plain, IReadOnlyList<PromptCatalogEntry> entries)
    {
        if (plain)
        {
            foreach (var line in PromptSetInventory.Format(entries))
            {
                Console.Out.WriteLine(line);
            }

            return;
        }

        if (entries.Count == 0)
        {
            AnsiConsole.MarkupLine($"[{Theme.Muted}]No prompt sets.[/]");
            return;
        }

        var table = CreateTable("Directory", "Enabled", "Effective", "Name", "Description", "Path");
        foreach (var entry in entries)
        {
            table.AddRow(
                Escape(entry.DirectoryName),
                YesNo(entry.Enabled),
                YesNo(entry.Effective),
                Escape(Label(entry)),
                Escape(Blank(entry.Description)),
                Escape(entry.ManifestPath));
        }

        AnsiConsole.Write(table);
    }

    private static void WriteSet(bool plain, PromptCatalogEntry entry)
    {
        if (plain)
        {
            foreach (var line in PromptSetInventory.Format(entry))
            {
                Console.Out.WriteLine(line);
            }

            return;
        }

        var table = CreateTable("Field", "Value");
        Add(table, "Directory", entry.DirectoryName);
        Add(table, "Enabled", entry.Enabled ? "yes" : "no");
        Add(table, "Effective", entry.Effective ? "yes" : "no");
        Add(table, "Name", Blank(entry.Title));
        Add(table, "Description", Blank(entry.Description));
        Add(table, "Path", entry.ManifestPath);
        if (entry.Error.Length > 0)
        {
            Add(table, "Error", entry.Error);
        }

        AnsiConsole.Write(table);
    }

    private static void WriteAttachments(bool plain, IReadOnlyList<PromptCatalogEntry> entries)
    {
        if (plain)
        {
            foreach (var line in PromptAttachmentInventory.Format(entries))
            {
                Console.Out.WriteLine(line);
            }

            return;
        }

        if (entries.Count == 0)
        {
            AnsiConsole.MarkupLine($"[{Theme.Muted}]No prompt attachments.[/]");
            return;
        }

        var table = CreateTable(
            "Directory",
            "Source",
            "Enabled",
            "Effective",
            "Name",
            "Description",
            "Order",
            "Path");
        foreach (var entry in entries)
        {
            table.AddRow(
                Escape(entry.DirectoryName),
                Escape(entry.Source),
                YesNo(entry.Enabled),
                YesNo(entry.Effective),
                Escape(Label(entry)),
                Escape(Blank(entry.Description)),
                Escape(entry.Order is int order ? order.ToString() : "-"),
                Escape(entry.ManifestPath));
        }

        AnsiConsole.Write(table);
    }

    private static void WriteAttachment(bool plain, PromptCatalogEntry entry)
    {
        if (plain)
        {
            foreach (var line in PromptAttachmentInventory.Format(entry))
            {
                Console.Out.WriteLine(line);
            }

            return;
        }

        var table = CreateTable("Field", "Value");
        Add(table, "Directory", entry.DirectoryName);
        Add(table, "Source", entry.Source);
        Add(table, "Enabled", entry.Enabled ? "yes" : "no");
        Add(table, "Effective", entry.Effective ? "yes" : "no");
        Add(table, "Name", Blank(entry.Title));
        Add(table, "Description", Blank(entry.Description));
        Add(table, "Order", entry.Order is int order ? order.ToString() : "-");
        Add(table, "Path", entry.ManifestPath);
        if (entry.Error.Length > 0)
        {
            Add(table, "Error", entry.Error);
        }

        AnsiConsole.Write(table);
    }

    private static string Label(PromptCatalogEntry entry) =>
        entry.Error.Length == 0 ? Blank(entry.Title) : entry.Error;

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
        table.AddRow($"[{Theme.Chrome}]{Markup.Escape(field)}[/]", Escape(value));

    private static string YesNo(bool value) =>
        value ? $"[{Theme.Ok}]Yes[/]" : $"[{Theme.Fail}]No[/]";

    private static string Blank(string value) => value.Length == 0 ? "-" : value;

    private static string Escape(string value) => Markup.Escape(value);

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
