using System.Text.Json;

using CrystalCode.Display.Paint;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Mcp;

using Spectre.Console;

namespace CrystalCode.Commands;

/// <summary>
/// Renders <c>crystal mcp</c>. The default is a Spectre table;
/// <c>--format text</c> prints aligned plain text. Output never includes
/// commands, arguments, environment, headers, or URLs, which can hold secrets.
/// </summary>
public static class McpConsole
{
    public static int Run(ExtensionSettings settings, string? server, string verb) =>
        Run(settings, server, verb, Console.Out, Console.Error);

    // The writers are a seam for tests. They apply to --format text only: the
    // table output goes through the static AnsiConsole, which owns its writer.
    internal static int Run(
        ExtensionSettings settings,
        string? server,
        string verb,
        TextWriter output,
        TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        if (!ExtensionConsole.TryParseFormat(settings.Format, out var plain, out var formatError))
        {
            Fail(error, plain: false, formatError);
            return 1;
        }

        if (!ExtensionConsole.TryParseSource(settings.Source, out var sourceName, out var sourceError))
        {
            Fail(error, plain, sourceError);
            return 1;
        }

        var home = CrystalHome.Resolve(settings.Home);
        var workspace = string.IsNullOrWhiteSpace(settings.Workspace)
            ? Path.GetFullPath(Environment.CurrentDirectory)
            : Path.GetFullPath(settings.Workspace);
        bool enabledInConfig;
        try
        {
            enabledInConfig = !File.Exists(home.ConfigPath) || new DiskSettingsStore(home).Load().Mcp;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException)
        {
            Fail(error, plain, "config.json could not be read.");
            return 1;
        }

        var source = sourceName switch
        {
            "home" => McpServerSource.Home,
            "project" => McpServerSource.Project,
            _ => (McpServerSource?)null
        };
        if (verb == "list")
        {
            WriteList(output, plain, McpInventory.List(home, workspace), enabledInConfig);
            return 0;
        }

        if (string.IsNullOrWhiteSpace(server))
        {
            Fail(error, plain, "Server is required.");
            return 1;
        }

        if (verb == "show")
        {
            if (!McpInventory.TryFind(home, workspace, server, source, out var found, out var findError) || found is null)
            {
                Fail(error, plain, findError);
                return 1;
            }

            WriteOne(output, plain, found);
            Off(output, plain, enabledInConfig);
            return 0;
        }

        var enabled = verb == "enable";
        if (!McpInventory.TrySetEnabled(
                home, workspace, server, source, enabled, out var entry, out var changed, out var setError)
            || entry is null)
        {
            Fail(error, plain, setError);
            return 1;
        }

        WriteOne(output, plain, entry);
        Line(output, plain, changed
            ? $"MCP server {entry.Name} {(enabled ? "enabled" : "disabled")}."
            : $"MCP server {entry.Name} is already {(enabled ? "enabled" : "disabled")}.");
        if (enabled)
        {
            Off(output, plain, enabledInConfig);
        }

        return 0;
    }

    private static void Off(TextWriter output, bool plain, bool enabledInConfig)
    {
        if (!enabledInConfig)
        {
            Line(output, plain, "MCP is off. Enabled servers will not connect until MCP is turned on.");
        }
    }

    private static void WriteList(TextWriter output, bool plain, IReadOnlyList<McpServerEntry> entries, bool enabledInConfig)
    {
        if (plain)
        {
            foreach (var line in McpInventory.Format(entries))
            {
                output.WriteLine(line);
            }
        }
        else if (entries.Count == 0)
        {
            AnsiConsole.MarkupLine($"[{Theme.Muted}]No MCP servers.[/]");
        }
        else
        {
            var table = CreateTable("Server", "Source", "Enabled", "Effective", "Transport", "Catalogs", "Path");
            foreach (var entry in entries)
            {
                table.AddRow(
                    Escape(entry.Name),
                    Escape(entry.Source.ToString()),
                    YesNo(entry.Enabled),
                    YesNo(entry.Effective),
                    Escape(entry.Error.Length > 0 ? entry.Error : McpInventory.Transport(entry.Transport)),
                    Escape(entry.Catalogs.Length == 0 ? "-" : entry.Catalogs),
                    Escape(entry.Path));
            }

            AnsiConsole.Write(table);
        }

        Off(output, plain, enabledInConfig);
    }

    private static void WriteOne(TextWriter output, bool plain, McpServerEntry entry)
    {
        if (plain)
        {
            foreach (var line in McpInventory.Format(entry))
            {
                output.WriteLine(line);
            }

            return;
        }

        var table = CreateTable("Field", "Value");
        Add(table, "Server", entry.Name);
        Add(table, "Source", entry.Source.ToString());
        Add(table, "Enabled", entry.Enabled switch { true => "Yes", false => "No", _ => "-" });
        Add(table, "Effective", entry.Effective ? "Yes" : "No");
        Add(table, "Transport", McpInventory.Transport(entry.Transport));
        Add(table, "Catalogs", entry.Catalogs.Length == 0 ? "-" : entry.Catalogs);
        Add(table, "Path", entry.Path);
        if (entry.Error.Length > 0)
        {
            Add(table, "Error", entry.Error);
        }

        AnsiConsole.Write(table);
    }

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

    private static string YesNo(bool? value) =>
        value switch
        {
            true => $"[{Theme.Ok}]Yes[/]",
            false => $"[{Theme.Fail}]No[/]",
            _ => $"[{Theme.Muted}]-[/]"
        };

    private static string Escape(string value) => Markup.Escape(value);

    private static void Line(TextWriter output, bool plain, string text)
    {
        if (plain)
        {
            output.WriteLine(text);
            return;
        }

        AnsiConsole.MarkupLine($"[{Theme.Muted}]{Markup.Escape(text)}[/]");
    }

    private static void Fail(TextWriter error, bool plain, string text)
    {
        if (plain)
        {
            error.WriteLine(text);
            return;
        }

        AnsiConsole.MarkupLine($"[{Theme.Fail}]{Markup.Escape(text)}[/]");
    }
}
