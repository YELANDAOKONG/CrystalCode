using System.Text.Json;

namespace CrystalCode.Tools.External;

/// <summary>
/// One contributed tool parsed from a tool set.
/// </summary>
public sealed record ExternalToolSpec
{
    public ExternalToolSpec(
        string name,
        string description,
        JsonElement schema,
        ExternalCatalogSelection catalogs,
        IReadOnlyList<string>? commandSuffix = null,
        IReadOnlyDictionary<string, string>? argv = null,
        IReadOnlyList<string>? pathArguments = null,
        ExternalApprovalMode? approval = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(catalogs);
        if (schema.ValueKind == JsonValueKind.Undefined)
        {
            throw new ArgumentException("Schema is required.", nameof(schema));
        }

        Name = name.Trim();
        Description = description.Trim();
        Schema = schema.Clone();
        Catalogs = catalogs;
        CommandSuffix = commandSuffix ?? [];
        Argv = argv ?? new Dictionary<string, string>(StringComparer.Ordinal);
        PathArguments = pathArguments ?? [];
        Approval = approval ?? ExternalApprovalMode.Inherit;
    }

    public string Name { get; }

    public string Description { get; }

    public JsonElement Schema { get; }

    public ExternalCatalogSelection Catalogs { get; }

    public IReadOnlyList<string> CommandSuffix { get; }

    public IReadOnlyDictionary<string, string> Argv { get; }

    public IReadOnlyList<string> PathArguments { get; }

    public ExternalApprovalMode Approval { get; }

    public override string ToString() => Name;
}
