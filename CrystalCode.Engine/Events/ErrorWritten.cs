namespace CrystalCode.Engine.Events;

/// <summary>
/// Plain error text for the transcript.
/// </summary>
public sealed record ErrorWritten(string Text) : SessionEvent;
