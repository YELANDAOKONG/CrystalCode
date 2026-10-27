namespace CrystalCode.Engine.Configuration;

/// <summary>Agent preferences; launch overrides are kept off the saved baseline.</summary>
public sealed record AgentSettings
{
    private int _maxConcurrent = 4;

    public bool Enabled { get; init; }

    public int MaxConcurrent
    {
        get => _maxConcurrent;
        init
        {
            if (value is < 1 or > 8)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Agent concurrency must be between 1 and 8.");
            }
            _maxConcurrent = value;
        }
    }

    public AgentLimits Limits { get; init; } = new();

    public AgentModelSettings General { get; init; } = new();
    public AgentModelSettings Explore { get; init; } = new();
    public AgentModelSettings Work { get; init; } = new();

    public AgentModelSettings For(string name) => name switch
    {
        "general" => General,
        "explore" => Explore,
        "work" => Work,
        _ => throw new ArgumentException("Unknown built-in Agent.", nameof(name))
    };

    public AgentSettings WithModel(string name, AgentModelSettings model) => name switch
    {
        "general" => this with { General = model },
        "explore" => this with { Explore = model },
        "work" => this with { Work = model },
        _ => throw new ArgumentException("Unknown built-in Agent.", nameof(name))
    };
}
