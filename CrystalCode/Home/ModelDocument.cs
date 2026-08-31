namespace CrystalCode.Home;

internal sealed class ModelDocument
{
    public int? ContextWindow { get; set; }

    public double? Temperature { get; set; }

    public double? TopP { get; set; }

    public int? MaxTokens { get; set; }

    public bool? Thinking { get; set; }

    public List<string>? ThinkingEfforts { get; set; }
}
