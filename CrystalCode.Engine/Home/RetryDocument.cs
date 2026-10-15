namespace CrystalCode.Engine.Home;

internal sealed class RetryDocument
{
    public bool? Enabled { get; set; }

    public int? MaximumRetries { get; set; }

    public double? InitialDelaySeconds { get; set; }

    public double? BackoffFactor { get; set; }

    public double? JitterFactor { get; set; }

    public double? MaximumDelaySeconds { get; set; }
}
