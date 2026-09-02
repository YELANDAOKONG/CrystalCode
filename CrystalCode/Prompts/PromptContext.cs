using System.Globalization;

namespace CrystalCode.Prompts;

/// <summary>
/// Host-owned values substituted into prompt templates.
/// </summary>
public sealed record PromptContext
{
    public const string DefaultProductName = "Crystal Code";

    private PromptContext(
        string workspace,
        string isGitRepo,
        string platform,
        string date,
        string provider,
        string model,
        string mode,
        string productName,
        string skills,
        string instructions)
    {
        Workspace = workspace;
        IsGitRepo = isGitRepo;
        Platform = platform;
        Date = date;
        Provider = provider;
        Model = model;
        Mode = mode;
        ProductName = productName;
        Skills = skills;
        Instructions = instructions;
    }

    public string Workspace { get; }

    public string IsGitRepo { get; }

    public string Platform { get; }

    public string Date { get; }

    public string Provider { get; }

    public string Model { get; }

    public string Mode { get; }

    public string ProductName { get; }

    public string Skills { get; }

    public string Instructions { get; }

    public string ModelLine => Provider + " / " + Model;

    public string EnvironmentBlock =>
        Workspace.Length > 0
            ? PromptEnvironment.FormatBlock(this)
            : string.Empty;

    public string InstructionsSection =>
        Instructions.Length > 0
            ? "## Workspace instructions\n" + Instructions
            : string.Empty;

    public static PromptContext Create(
        string workspaceRoot,
        string provider,
        string model,
        string mode,
        string skills,
        string instructions,
        DateTimeOffset? now = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(mode);
        ArgumentNullException.ThrowIfNull(skills);
        ArgumentNullException.ThrowIfNull(instructions);

        var snapshot = PromptEnvironment.CreateSnapshot(workspaceRoot, provider, model, now);
        return new PromptContext(
            snapshot.Workspace,
            snapshot.IsGitRepo,
            snapshot.Platform,
            snapshot.Date,
            snapshot.Provider,
            snapshot.Model,
            mode.Trim(),
            DefaultProductName,
            skills.Trim(),
            instructions.Trim());
    }

    public static PromptContext InstructionsOnly(string instructions)
    {
        ArgumentNullException.ThrowIfNull(instructions);
        return new PromptContext(
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            DefaultProductName,
            string.Empty,
            instructions.Trim());
    }

    public PromptContext WithMode(string mode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mode);
        return new PromptContext(
            Workspace,
            IsGitRepo,
            Platform,
            Date,
            Provider,
            Model,
            mode.Trim(),
            ProductName,
            Skills,
            Instructions);
    }
}
