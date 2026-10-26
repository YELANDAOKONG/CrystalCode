namespace CrystalCode.Server.Security;

/// <summary>
/// One accepted token. Only the SHA-256 hash of the secret is kept.
/// </summary>
/// <param name="Id">Public id used to revoke the token.</param>
/// <param name="Hash">SHA-256 of the UTF-8 secret.</param>
/// <param name="Scope">Highest scope the token grants.</param>
/// <param name="Label">Operator label, never the secret.</param>
public sealed record ServerToken(string Id, byte[] Hash, ServerScope Scope, string Label);
