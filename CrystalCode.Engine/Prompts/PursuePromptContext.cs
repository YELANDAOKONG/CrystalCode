namespace CrystalCode.Engine.Prompts;

/// <summary>
/// Per-request values for the pursue user and continuation templates.
/// <see cref="VerdictFormat"/> is host-owned. <see cref="Resumed"/> is
/// <c>yes</c> on the first segment of a resume and empty otherwise.
/// </summary>
public sealed record PursuePromptContext(
    string Objective,
    string Conversation = "",
    string ModeName = "",
    string VerdictFormat = PursuePrompt.VerdictFormat,
    string Reason = "",
    string NextStep = "",
    string RemainingContinuations = "",
    string Resumed = "");
