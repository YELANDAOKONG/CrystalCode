namespace CrystalCode.Prompts;

/// <summary>
/// Caller-authored text for context-summary generation.
/// </summary>
public static class CompactionPrompt
{
    public const string Marker = "## Earlier context";

    public const string SystemText =
        """
        You are a context summarization agent for {{product_name}}. You are given a conversation between a user and an agent. Your goal is to produce a structured summary matching the format specified so another coding agent can continue the work.

        Always follow the exact output structure requested by the user prompt. Keep every section, preserve exact file paths and identifiers when known, and prefer terse bullets over paragraphs.

        Do not continue the conversation. Do not respond to any questions in the conversation. Only output the structured summary in the exact format requested by the user prompt. Respond in the same language as the conversation.
        Do not invent facts. Do not include secrets or credentials.

        {{env}}
        """;

    public const string UserTemplate =
        """
        Here is the conversation so far:

        <conversation>
        {{conversation}}
        </conversation>

        {{prior_summary_section}}

        {{summary_task}}

        {{output_template}}

        {{todos_section}}
        """;

    public const string OutputTemplateText =
        """
        Output exactly the Markdown structure shown inside <template> and keep the section order unchanged. Do not include the <template> tags in your response.
        <template>
        ## Objective
        - [one or two brief sentences describing what the user is trying to accomplish]

        ## Important Details
        - [constraints/preferences, decisions and why, important facts/assumptions, exact context needed to continue, or "(none)"]

        ## Work State
        ### Completed
        - [finished work, verified facts, or changes made; otherwise "(none)"]

        ### Active
        - [current work, partial changes, or investigation state; otherwise "(none)"]

        ### Blocked
        - [blockers, failing commands, or unknowns; otherwise "(none)"]

        ## Next Move
        1. [immediate concrete action, or "(none)"]
        2. [next action if known, or "(none)"]

        ## Relevant Files
        - [file or directory path: why it matters, or "(none)"]
        </template>

        Rules:
        - Keep every section, even when empty.
        - Use terse bullets, not prose paragraphs.
        - Preserve exact file paths, symbols, commands, error strings, URLs, and identifiers when known.
        - Do not mention the summary process or that context was compacted.
        """;

    public static string ComposeSystem(PromptContext context) =>
        PromptBinder.Apply(SystemText, context.WithMode("compaction"));

    public static string UserText(string conversation, string todos, string? previousSummary)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(todos);
        return PromptBinder.Apply(
            UserTemplate,
            new PromptBinding(Compaction: CreateUserContext(conversation, todos, previousSummary)));
    }

    public static CompactionPromptContext CreateUserContext(
        string conversation,
        string todos,
        string? previousSummary)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(todos);
        var priorSummarySection = string.Empty;
        var summaryTask =
            "Create a new anchored summary from the conversation history in the <conversation> tags above so another coding agent can continue the work.";
        if (!string.IsNullOrWhiteSpace(previousSummary))
        {
            priorSummarySection =
                """
                Here is the summary of the conversation before the <conversation> above:

                <prior-summary>
                """
                + previousSummary.Trim()
                + """

                </prior-summary>

                The <prior-summary> summarizes everything that happened before the <conversation>. Construct a new summary that combines both. The <prior-summary> is discarded after this: anything you do not carry into the new summary is lost.

                When combining:
                - Carry forward objectives, constraints, user directives, decisions, and parallel workstreams from the <prior-summary> even when the <conversation> does not mention them. Drop only what is finished and no longer needed.
                - The <conversation> is more recent than the <prior-summary>. Where they conflict, the conversation wins: state the corrected fact and drop the old claim.
                - Add new progress, decisions, constraints, and context from the conversation.
                - Move completed work from "Active" to "Completed".
                - If a blocker has been resolved, update the summary to reflect that while keeping any details still needed to continue the work.
                - Update "Objective" and "Next Move" to reflect the current work state.
                """;
            summaryTask = string.Empty;
        }

        var todosSection = string.Empty;
        var extra = todos.Trim();
        if (extra.Length > 0 && extra != "No todos.")
        {
            todosSection = "Open todos to preserve:\n" + extra;
        }

        return new CompactionPromptContext(
            conversation.Trim(),
            priorSummarySection,
            summaryTask,
            OutputTemplateText,
            todosSection);
    }
}
