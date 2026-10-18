namespace CrystalCode.Engine.Events;

/// <summary>
/// The operator took a user message back before the model produced output.
/// Text is the submitted message, including trusted image markers.
/// </summary>
public sealed record UserMessageRecalled(string Text) : SessionEvent;
