namespace CrystalCode.Engine.Home;

internal sealed class SettingsDocument
{
    public CrystalCode.Engine.Configuration.AgentSettings? Agents { get; set; }

    public string? Provider { get; set; }

    public string? Model { get; set; }

    public string? Approval { get; set; }

    public string? ThinkingEffort { get; set; }

    public System.Text.Json.JsonElement? ContextSelections { get; set; }

    public System.Text.Json.JsonElement? TierSelections { get; set; }

    public System.Text.Json.JsonElement? SpeedSelections { get; set; }

    public bool? Skills { get; set; }

    public bool? ExternalTools { get; set; }

    public bool? Mcp { get; set; }

    public bool? Plugins { get; set; }

    public bool? WorkspaceTrust { get; set; }

    public RuleFilesDocument? RuleFiles { get; set; }

    public ExternalToolApprovalDocument? ExternalToolApproval { get; set; }

    public McpToolApprovalDocument? McpToolApproval { get; set; }

    public bool? EstimatedTokens { get; set; }

    public bool? TodoBar { get; set; }

    public int? TodoBarRows { get; set; }

    public bool? VerboseTools { get; set; }

    public bool? VerboseCommands { get; set; }

    public bool? VerboseResults { get; set; }

    public bool? VerboseCalls { get; set; }

    public bool? VerboseApprovals { get; set; }

    public bool? VerboseReview { get; set; }

    public bool? VerboseThinking { get; set; }

    public string? PromptSet { get; set; }

    public List<string>? PromptAttachments { get; set; }

    public string? ExportDirectory { get; set; }

    public bool? CustomStatusLine { get; set; }

    public List<string>? StatusLine { get; set; }

    public double? CompactionThreshold { get; set; }

    public int? ReservedOutputTokens { get; set; }

    public bool? ShowCompactionSummary { get; set; }

    public bool? ShowAutomaticCompactionSummary { get; set; }

    public System.Text.Json.JsonElement? ExecutionBudget { get; set; }

    public System.Text.Json.JsonElement? BashTimeoutSeconds { get; set; }

    public System.Text.Json.JsonElement? BackgroundBashTimeoutSeconds { get; set; }

    public System.Text.Json.JsonElement? Providers { get; set; }

    public ApprovalModelDocument? ApprovalModel { get; set; }

    public ExtendedApprovalDocument? ExtendedApproval { get; set; }

    public OperatorNotifyDocument? OperatorNotify { get; set; }

    public ServerHostDocument? Server { get; set; }

    public BridgeHostDocument? Bridge { get; set; }

    public CompactionModelDocument? CompactionModel { get; set; }

    public MicroCompactionDocument? MicroCompaction { get; set; }

    public ImageModelDocument? ImageModel { get; set; }

    public RetryDocument? Retry { get; set; }

    public RetryDocument? ApprovalRetry { get; set; }

    public System.Text.Json.JsonElement? DisabledBuiltInTools { get; set; }
}
