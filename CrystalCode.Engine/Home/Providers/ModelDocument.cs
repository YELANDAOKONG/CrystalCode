using System.Text.Json;

namespace CrystalCode.Engine.Home.Providers;

internal sealed class ModelDocument
{
    public int? ContextWindow { get; set; }

    public List<int>? ContextWindows { get; set; }

    public JsonElement? Options { get; set; }

    public int? MaxTokens { get; set; }

    public bool? Thinking { get; set; }

    public List<string>? ThinkingEfforts { get; set; }

    public bool? ImageInput { get; set; }

    public bool? ThinkingCanDisable { get; set; }

    public bool? AudioInput { get; set; }

    public bool? VideoInput { get; set; }

    public bool? FileInput { get; set; }

    public bool? TextTools { get; set; }

    public List<string>? ServiceTiers { get; set; }

    public List<string>? Speeds { get; set; }
}
