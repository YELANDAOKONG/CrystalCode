namespace CrystalCode.Server.Hosting;

/// <summary>
/// A finished request: its HTTP status and a JSON body, or a problem with an
/// English detail. Kept by <see cref="RequestLedger"/> for retries.
/// </summary>
public sealed record RequestOutcome(int Status, string? Json, string? Problem)
{
    public static RequestOutcome Ok(string json) => new(200, json, null);

    public static RequestOutcome Error(int status, string detail) => new(status, null, detail);
}
