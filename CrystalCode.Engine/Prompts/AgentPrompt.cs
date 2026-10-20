namespace CrystalCode.Engine.Prompts;

internal static class AgentPrompt
{
    public const string Rules = "You are a child Agent in a parent coding session. Complete only the delegated task. "
        + "You cannot ask the operator, start background shell tasks, or delegate. "
        + "Return a concise report with evidence, changed files, checks performed, and any blockers. "
        + "Do not invent missing facts. Return blockers to the parent. Other Agents share this workspace; "
        + "do not overwrite their changes. Only edit files within the delegated scope.";

    public static string For(string name) => name switch
    {
        "explore" => "Find evidence and exact file paths. You are strictly read-only. Stop once the question is answered.",
        "work" => "Implement the scoped change and verify it. Do not expand the task.",
        "general" => "Solve the bounded task and return the result to the parent.",
        _ => "Complete the delegated task."
    };
}
