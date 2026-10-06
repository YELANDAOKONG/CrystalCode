using System.Text.Json;

using CrystalCode.Display.Paint;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Plugins.Disk;
using CrystalCode.Engine.Tools.External;

using Spectre.Console;

namespace CrystalCode.Commands;

/// <summary>
/// Renders plugin and tool set manifests. The default is a Spectre table.
/// <c>--format text</c> prints aligned plain text.
/// </summary>
public static class ExtensionConsole
{
    public static int Plugins(ExtensionSettings settings, string? directory, string verb) =>
        Run(settings, directory, verb, plugins: true);

    public static int Tools(ExtensionSettings settings, string? directory, string verb) =>
        Run(settings, directory, verb, plugins: false);

    private static int Run(ExtensionSettings settings, string? directory, string verb, bool plugins)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!TryParseFormat(settings.Format, out var plain, out var formatError))
        {
            Fail(plain: false, formatError);
            return 1;
        }

        if (!TryParseSource(settings.Source, out var sourceName, out var sourceError))
        {
            Fail(plain, sourceError);
            return 1;
        }

        var home = CrystalHome.Resolve(settings.Home);
        var workspace = string.IsNullOrWhiteSpace(settings.Workspace)
            ? Path.GetFullPath(Environment.CurrentDirectory)
            : Path.GetFullPath(settings.Workspace);
        bool discovery;
        try
        {
            discovery = DiscoveryEnabled(home, plugins);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException)
        {
            Fail(plain, "config.json could not be read.");
            return 1;
        }
        if (verb == "list")
        {
            if (plugins)
            {
                WritePlugins(plain, PluginInventory.List(home, workspace), discovery);
            }
            else
            {
                WriteTools(plain, ToolSetInventory.List(home, workspace), discovery);
            }

            return 0;
        }

        if (string.IsNullOrWhiteSpace(directory))
        {
            Fail(plain, "Directory is required.");
            return 1;
        }

        if (verb == "show")
        {
            return plugins
                ? ShowPlugin(plain, home, workspace, directory, sourceName, discovery)
                : ShowTool(plain, home, workspace, directory, sourceName, discovery);
        }

        var enabled = verb == "enable";
        return plugins
            ? SetPlugin(plain, home, workspace, directory, sourceName, enabled, discovery)
            : SetTool(plain, home, workspace, directory, sourceName, enabled, discovery);
    }

    public static bool TryParseFormat(string? format, out bool plain, out string error)
    {
        plain = false;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(format))
        {
            return true;
        }

        if (format.Trim().Equals("text", StringComparison.OrdinalIgnoreCase))
        {
            plain = true;
            return true;
        }

        error = "Format must be text.";
        return false;
    }

    public static bool TryParseSource(string? source, out string? name, out string error)
    {
        name = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(source))
        {
            return true;
        }

        var token = source.Trim().ToLowerInvariant();
        if (token is "home" or "project")
        {
            name = token;
            return true;
        }

        error = "Source must be home or project.";
        return false;
    }

    private static int ShowPlugin(
        bool plain,
        CrystalHome home,
        string workspace,
        string directory,
        string? sourceName,
        bool discovery)
    {
        if (!PluginInventory.TryFind(home, workspace, directory, ToPluginSource(sourceName), out var entry, out var error)
            || entry is null)
        {
            Fail(plain, error);
            return 1;
        }

        WritePlugin(plain, entry);
        if (!discovery)
        {
            Line(plain, PluginInventory.DiscoveryOff);
        }

        return 0;
    }

    private static int ShowTool(
        bool plain,
        CrystalHome home,
        string workspace,
        string directory,
        string? sourceName,
        bool discovery)
    {
        if (!ToolSetInventory.TryFind(home, workspace, directory, ToToolSource(sourceName), out var entry, out var error)
            || entry is null)
        {
            Fail(plain, error);
            return 1;
        }

        WriteTool(plain, entry);
        if (!discovery)
        {
            Line(plain, ToolSetInventory.DiscoveryOff);
        }

        return 0;
    }

    private static int SetPlugin(
        bool plain,
        CrystalHome home,
        string workspace,
        string directory,
        string? sourceName,
        bool enabled,
        bool discovery)
    {
        if (!PluginInventory.TrySetEnabled(
                home,
                workspace,
                directory,
                ToPluginSource(sourceName),
                enabled,
                out var entry,
                out var changed,
                out var error)
            || entry is null)
        {
            Fail(plain, error);
            return 1;
        }

        WritePlugin(plain, entry);
        Line(plain, changed
            ? $"Plugin {entry.DirectoryName} {(enabled ? "enabled" : "disabled")}."
            : $"Plugin {entry.DirectoryName} is already {(enabled ? "enabled" : "disabled")}.");
        if (enabled && !discovery)
        {
            Line(plain, PluginInventory.DiscoveryOff);
        }

        return 0;
    }

    private static int SetTool(
        bool plain,
        CrystalHome home,
        string workspace,
        string directory,
        string? sourceName,
        bool enabled,
        bool discovery)
    {
        if (!ToolSetInventory.TrySetEnabled(
                home,
                workspace,
                directory,
                ToToolSource(sourceName),
                enabled,
                out var entry,
                out var changed,
                out var error)
            || entry is null)
        {
            Fail(plain, error);
            return 1;
        }

        WriteTool(plain, entry);
        Line(plain, changed
            ? $"Tool set {entry.DirectoryName} {(enabled ? "enabled" : "disabled")}."
            : $"Tool set {entry.DirectoryName} is already {(enabled ? "enabled" : "disabled")}.");
        if (enabled && !discovery)
        {
            Line(plain, ToolSetInventory.DiscoveryOff);
        }

        return 0;
    }

    private static void WritePlugins(bool plain, IReadOnlyList<PluginEntry> entries, bool discovery)
    {
        if (plain)
        {
            foreach (var line in PluginInventory.Format(entries))
            {
                Console.Out.WriteLine(line);
            }
        }
        else if (entries.Count == 0)
        {
            AnsiConsole.MarkupLine($"[{Theme.Muted}]No plugins.[/]");
        }
        else
        {
            var table = CreateTable("Directory", "Source", "Enabled", "Effective", "Path", "Assembly", "Type");
            foreach (var entry in entries)
            {
                table.AddRow(
                    Escape(entry.DirectoryName),
                    Escape(Source(entry.Source)),
                    YesNo(entry.Enabled),
                    YesNo(entry.Effective),
                    Escape(entry.ManifestPath),
                    Escape(Blank(entry.AssemblyPath)),
                    Escape(entry.Error.Length == 0 ? Blank(entry.TypeName) : entry.Error));
            }

            AnsiConsole.Write(table);
        }

        if (!discovery)
        {
            Line(plain, PluginInventory.DiscoveryOff);
        }
    }

    private static void WriteTools(bool plain, IReadOnlyList<ToolSetEntry> entries, bool discovery)
    {
        if (plain)
        {
            foreach (var line in ToolSetInventory.Format(entries))
            {
                Console.Out.WriteLine(line);
            }
        }
        else if (entries.Count == 0)
        {
            AnsiConsole.MarkupLine($"[{Theme.Muted}]No tool sets.[/]");
        }
        else
        {
            var table = CreateTable(
                "Directory",
                "Source",
                "Enabled",
                "Effective",
                "Path",
                "Runner",
                "Catalogs",
                "Approval",
                "Tools");
            foreach (var entry in entries)
            {
                var tools = entry.Error.Length > 0
                    ? entry.Error
                    : entry.Tools.Count == 0 ? "-" : string.Join(", ", entry.Tools);
                table.AddRow(
                    Escape(entry.DirectoryName),
                    Escape(Source(entry.Source)),
                    YesNo(entry.Enabled),
                    YesNo(entry.Effective),
                    Escape(entry.ManifestPath),
                    Escape(Blank(entry.Runner)),
                    Escape(Blank(entry.Catalogs)),
                    Escape(Blank(entry.Approval)),
                    Escape(tools));
            }

            AnsiConsole.Write(table);
        }

        if (!discovery)
        {
            Line(plain, ToolSetInventory.DiscoveryOff);
        }
    }

    private static void WritePlugin(bool plain, PluginEntry entry)
    {
        if (plain)
        {
            foreach (var line in PluginInventory.Format(entry))
            {
                Console.Out.WriteLine(line);
            }

            return;
        }

        var table = CreateTable("Field", "Value");
        Add(table, "Directory", entry.DirectoryName);
        Add(table, "Source", Source(entry.Source));
        Add(table, "Enabled", PlainFlag(entry.Enabled));
        Add(table, "Effective", entry.Effective ? "Yes" : "No");
        Add(table, "Path", entry.ManifestPath);
        Add(table, "Assembly", Blank(entry.AssemblyPath));
        Add(table, "Type", Blank(entry.TypeName));
        if (entry.Error.Length > 0)
        {
            Add(table, "Error", entry.Error);
        }

        AnsiConsole.Write(table);
    }

    private static void WriteTool(bool plain, ToolSetEntry entry)
    {
        if (plain)
        {
            foreach (var line in ToolSetInventory.Format(entry))
            {
                Console.Out.WriteLine(line);
            }

            return;
        }

        var table = CreateTable("Field", "Value");
        Add(table, "Directory", entry.DirectoryName);
        Add(table, "Source", Source(entry.Source));
        Add(table, "Enabled", PlainFlag(entry.Enabled));
        Add(table, "Effective", entry.Effective ? "Yes" : "No");
        Add(table, "Path", entry.ManifestPath);
        Add(table, "Runner", Blank(entry.Runner));
        Add(table, "Catalogs", Blank(entry.Catalogs));
        Add(table, "Approval", Blank(entry.Approval));
        Add(table, "Tools", entry.Tools.Count == 0 ? "-" : string.Join(", ", entry.Tools));
        if (entry.Error.Length > 0)
        {
            Add(table, "Error", entry.Error);
        }

        AnsiConsole.Write(table);
    }

    private static bool DiscoveryEnabled(CrystalHome home, bool plugins)
    {
        if (!File.Exists(home.ConfigPath))
        {
            return true;
        }

        var settings = new SettingsStore(home).Load();
        return plugins ? settings.Plugins : settings.ExternalTools;
    }

    private static PluginSource? ToPluginSource(string? source) =>
        source switch
        {
            "home" => PluginSource.Home,
            "project" => PluginSource.Project,
            _ => null
        };

    private static ExternalToolSource? ToToolSource(string? source) =>
        source switch
        {
            "home" => ExternalToolSource.Home,
            "project" => ExternalToolSource.Project,
            _ => null
        };

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

    private static string PlainFlag(bool? value) =>
        value switch
        {
            true => "Yes",
            false => "No",
            _ => "-"
        };

    private static string Source(PluginSource source) =>
        source == PluginSource.Home ? "Home" : "Project";

    private static string Source(ExternalToolSource source) =>
        source == ExternalToolSource.Home ? "Home" : "Project";

    private static string Blank(string value) => value.Length == 0 ? "-" : value;

    private static string Escape(string value) => Markup.Escape(value);

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
