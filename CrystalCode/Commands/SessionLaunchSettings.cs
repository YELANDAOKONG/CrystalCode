using Spectre.Console.Cli;

using CrystalCode.Engine.Launch;

namespace CrystalCode.Commands;

/// <summary>
/// Session options shared by <c>crystal</c>, <c>crystal space</c>, and
/// <c>crystal run</c>. Values override the saved configuration for this
/// process. They are not written at startup.
/// </summary>
public class SessionLaunchSettings : CommandSettings
{
    [CommandOption("-p|--provider <PROVIDER>")]
    public string? Provider { get; init; }

    [CommandOption("-m|--model <MODEL>")]
    public string? Model { get; init; }

    [CommandOption("--home <PATH>")]
    public string? Home { get; init; }

    [CommandOption("--allow-read <PATH>")]
    public string[] AllowRead { get; init; } = [];

    [CommandOption("--allow-write <PATH>")]
    public string[] AllowWrite { get; init; } = [];

    [CommandOption("--approval <MODE>")]
    public string? Approval { get; init; }

    [CommandOption("--approval-model <on|off>")]
    public string? ApprovalModel { get; init; }

    [CommandOption("--approval-provider <PROVIDER>")]
    public string? ApprovalProvider { get; init; }

    [CommandOption("--approval-model-id <MODEL>")]
    public string? ApprovalModelId { get; init; }

    [CommandOption("--approval-thinking <EFFORT>")]
    public string? ApprovalThinking { get; init; }

    [CommandOption("--approval-tier <TOKEN>")]
    public string? ApprovalTier { get; init; }

    [CommandOption("--approval-speed <TOKEN>")]
    public string? ApprovalSpeed { get; init; }

    [CommandOption("--approval-context <BUDGET>")]
    public string? ApprovalContext { get; init; }

    [CommandOption("--extended-approval <on|off>")]
    public string? ExtendedApproval { get; init; }

    [CommandOption("--extended-approval-provider <PROVIDER>")]
    public string? ExtendedApprovalProvider { get; init; }

    [CommandOption("--extended-approval-model-id <MODEL>")]
    public string? ExtendedApprovalModelId { get; init; }

    [CommandOption("--extended-approval-thinking <EFFORT>")]
    public string? ExtendedApprovalThinking { get; init; }

    [CommandOption("--extended-approval-tier <TOKEN>")]
    public string? ExtendedApprovalTier { get; init; }

    [CommandOption("--extended-approval-speed <TOKEN>")]
    public string? ExtendedApprovalSpeed { get; init; }

    [CommandOption("--extended-approval-context <BUDGET>")]
    public string? ExtendedApprovalContext { get; init; }

    [CommandOption("--extended-approval-trigger <low|medium|high>")]
    public string? ExtendedApprovalTrigger { get; init; }

    [CommandOption("--extended-approval-primary <on|off>")]
    public string? ExtendedApprovalPrimary { get; init; }

    [CommandOption("--compaction-model <on|off>")]
    public string? CompactionModel { get; init; }

    [CommandOption("--compaction-provider <PROVIDER>")]
    public string? CompactionProvider { get; init; }

    [CommandOption("--compaction-model-id <MODEL>")]
    public string? CompactionModelId { get; init; }

    [CommandOption("--compaction-thinking <EFFORT>")]
    public string? CompactionThinking { get; init; }

    [CommandOption("--compaction-tier <TOKEN>")]
    public string? CompactionTier { get; init; }

    [CommandOption("--compaction-speed <TOKEN>")]
    public string? CompactionSpeed { get; init; }

    [CommandOption("--compaction-context <BUDGET>")]
    public string? CompactionContext { get; init; }

    [CommandOption("--topic <on|off>")]
    public string? Topic { get; init; }

    [CommandOption("--topic-provider <PROVIDER>")]
    public string? TopicProvider { get; init; }

    [CommandOption("--topic-model-id <MODEL>")]
    public string? TopicModelId { get; init; }

    [CommandOption("--topic-thinking <EFFORT>")]
    public string? TopicThinking { get; init; }

    [CommandOption("--topic-tier <TOKEN>")]
    public string? TopicTier { get; init; }

    [CommandOption("--topic-speed <TOKEN>")]
    public string? TopicSpeed { get; init; }

    [CommandOption("--topic-context <BUDGET>")]
    public string? TopicContext { get; init; }

    [CommandOption("--image-model <on|off>")]
    public string? ImageModel { get; init; }

    [CommandOption("--image-provider <PROVIDER>")]
    public string? ImageProvider { get; init; }

    [CommandOption("--image-model-id <MODEL>")]
    public string? ImageModelId { get; init; }

    [CommandOption("--image-thinking <EFFORT>")]
    public string? ImageThinking { get; init; }

    [CommandOption("--image-tier <TOKEN>")]
    public string? ImageTier { get; init; }

    [CommandOption("--image-speed <TOKEN>")]
    public string? ImageSpeed { get; init; }

    [CommandOption("--image-context <BUDGET>")]
    public string? ImageContext { get; init; }

    [CommandOption("--audio-model <on|off>")]
    public string? AudioModel { get; init; }

    [CommandOption("--audio-provider <PROVIDER>")]
    public string? AudioProvider { get; init; }

    [CommandOption("--audio-model-id <MODEL>")]
    public string? AudioModelId { get; init; }

    [CommandOption("--audio-thinking <EFFORT>")]
    public string? AudioThinking { get; init; }

    [CommandOption("--audio-tier <TOKEN>")]
    public string? AudioTier { get; init; }

    [CommandOption("--audio-speed <TOKEN>")]
    public string? AudioSpeed { get; init; }

    [CommandOption("--audio-context <BUDGET>")]
    public string? AudioContext { get; init; }

    [CommandOption("--video-model <on|off>")]
    public string? VideoModel { get; init; }

    [CommandOption("--video-provider <PROVIDER>")]
    public string? VideoProvider { get; init; }

    [CommandOption("--video-model-id <MODEL>")]
    public string? VideoModelId { get; init; }

    [CommandOption("--video-thinking <EFFORT>")]
    public string? VideoThinking { get; init; }

    [CommandOption("--video-tier <TOKEN>")]
    public string? VideoTier { get; init; }

    [CommandOption("--video-speed <TOKEN>")]
    public string? VideoSpeed { get; init; }

    [CommandOption("--video-context <BUDGET>")]
    public string? VideoContext { get; init; }

    [CommandOption("--plan")]
    public bool Plan { get; init; }

    [CommandOption("--work")]
    public bool Work { get; init; }

    [CommandOption("--mode <NAME>")]
    public string? Mode { get; init; }

    [CommandOption("--thinking <EFFORT>")]
    public string? Thinking { get; init; }

    [CommandOption("--context <BUDGET>")]
    public string? Context { get; init; }

    [CommandOption("--tier <TOKEN>")]
    public string? Tier { get; init; }

    [CommandOption("--speed|--fast <TOKEN>")]
    public string? Speed { get; init; }

    [CommandOption("--prompt-set <NAME>")]
    public string? PromptSet { get; init; }

    [CommandOption("--prompt-attachments <on|off>")]
    public string? PromptAttachments { get; init; }

    [CommandOption("--prompt-attachment <NAME>")]
    public string[] PromptAttachment { get; init; } = [];

    [CommandOption("--agents <on|off>")]
    public string? Agents { get; init; }

    [CommandOption("--agents-concurrency <COUNT>")]
    public int? AgentsConcurrency { get; init; }

    [CommandOption("--agents-model-calls <COUNT>")]
    public string? AgentsModelCalls { get; init; }

    [CommandOption("--agents-tool-calls <COUNT>")]
    public string? AgentsToolCalls { get; init; }

    [CommandOption("--agents-duration <SECONDS>")]
    public string? AgentsDuration { get; init; }

    [CommandOption("--skills <on|off>")]
    public string? Skills { get; init; }

    [CommandOption("--external-tools <on|off>")]
    public string? ExternalTools { get; init; }

    [CommandOption("--mcp <on|off>")]
    public string? Mcp { get; init; }

    [CommandOption("--plugins <on|off>")]
    public string? Plugins { get; init; }

    [CommandOption("--rules <VALUE>")]
    public string? Rules { get; init; }

    [CommandOption("--theme <NAME>")]
    public string? Theme { get; init; }

    [CommandOption("--model-calls <COUNT>")]
    public string? ModelCalls { get; init; }

    [CommandOption("--tool-calls <COUNT>")]
    public string? ToolCalls { get; init; }

    [CommandOption("--duration <SECONDS>")]
    public string? Duration { get; init; }

    [CommandOption("--bash-timeout <SECONDS>")]
    public string? BashTimeout { get; init; }

    [CommandOption("--disable-tool <NAME>")]
    public string[] DisableTool { get; init; } = [];

    public SessionLaunchOptions ToOptions() =>
        new()
        {
            Provider = Provider,
            Model = Model,
            Home = Home,
            AllowRead = AllowRead,
            AllowWrite = AllowWrite,
            Approval = Approval,
            ApprovalModel = ApprovalModel,
            ApprovalProvider = ApprovalProvider,
            ApprovalModelId = ApprovalModelId,
            ApprovalThinking = ApprovalThinking,
            ApprovalTier = ApprovalTier,
            ApprovalSpeed = ApprovalSpeed,
            ApprovalContext = ApprovalContext,
            ExtendedApproval = ExtendedApproval,
            ExtendedApprovalProvider = ExtendedApprovalProvider,
            ExtendedApprovalModelId = ExtendedApprovalModelId,
            ExtendedApprovalThinking = ExtendedApprovalThinking,
            ExtendedApprovalTier = ExtendedApprovalTier,
            ExtendedApprovalSpeed = ExtendedApprovalSpeed,
            ExtendedApprovalContext = ExtendedApprovalContext,
            ExtendedApprovalTrigger = ExtendedApprovalTrigger,
            ExtendedApprovalPrimary = ExtendedApprovalPrimary,
            CompactionModel = CompactionModel,
            CompactionProvider = CompactionProvider,
            CompactionModelId = CompactionModelId,
            CompactionThinking = CompactionThinking,
            CompactionTier = CompactionTier,
            CompactionSpeed = CompactionSpeed,
            CompactionContext = CompactionContext,
            Topic = Topic,
            TopicProvider = TopicProvider,
            TopicModelId = TopicModelId,
            TopicThinking = TopicThinking,
            TopicTier = TopicTier,
            TopicSpeed = TopicSpeed,
            TopicContext = TopicContext,
            ImageModel = ImageModel,
            ImageProvider = ImageProvider,
            ImageModelId = ImageModelId,
            ImageThinking = ImageThinking,
            ImageTier = ImageTier,
            ImageSpeed = ImageSpeed,
            ImageContext = ImageContext,
            AudioModel = AudioModel,
            AudioProvider = AudioProvider,
            AudioModelId = AudioModelId,
            AudioThinking = AudioThinking,
            AudioTier = AudioTier,
            AudioSpeed = AudioSpeed,
            AudioContext = AudioContext,
            VideoModel = VideoModel,
            VideoProvider = VideoProvider,
            VideoModelId = VideoModelId,
            VideoThinking = VideoThinking,
            VideoTier = VideoTier,
            VideoSpeed = VideoSpeed,
            VideoContext = VideoContext,
            Plan = Plan,
            Work = Work,
            Mode = Mode,
            Thinking = Thinking,
            Context = Context,
            Tier = Tier,
            Speed = Speed,
            PromptSet = PromptSet,
            PromptAttachments = PromptAttachments,
            PromptAttachment = PromptAttachment,
            Agents = Agents,
            AgentsConcurrency = AgentsConcurrency,
            AgentsModelCalls = AgentsModelCalls,
            AgentsToolCalls = AgentsToolCalls,
            AgentsDuration = AgentsDuration,
            Skills = Skills,
            ExternalTools = ExternalTools,
            Mcp = Mcp,
            Plugins = Plugins,
            Rules = Rules,
            Theme = Theme,
            ModelCalls = ModelCalls,
            ToolCalls = ToolCalls,
            Duration = Duration,
            BashTimeout = BashTimeout,
            DisableTool = DisableTool
        };
}
