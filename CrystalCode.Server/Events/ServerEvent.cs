namespace CrystalCode.Server.Events;

/// <summary>
/// One event ready to send: its sequence number, the client it is for (null
/// for everyone), and its serialized JSON envelope.
/// </summary>
public sealed record ServerEvent(long Sequence, string? Target, string Type, string Json);
