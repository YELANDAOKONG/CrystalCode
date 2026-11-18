namespace CrystalCode.Providers.Responses;

/// <summary>
/// Request shape switches for endpoints that differ from the public API.
/// </summary>
/// <param name="SystemAsInstructions">
/// Send system text in <c>instructions</c> instead of as system message items.
/// </param>
/// <param name="IncludeEncryptedReasoning">
/// Ask for <c>reasoning.encrypted_content</c> so reasoning can be replayed.
/// </param>
/// <param name="OmitSampling">
/// Leave out <c>temperature</c>, <c>top_p</c>, and <c>max_output_tokens</c>.
/// </param>
/// <param name="SystemAsDeveloper">
/// Write system messages as developer messages in input, overriding <c>SystemAsInstructions</c>.
/// </param>
public sealed record ResponsesRequestShape(
    bool SystemAsInstructions = false,
    bool IncludeEncryptedReasoning = true,
    bool OmitSampling = false,
    bool SystemAsDeveloper = false)
{
    internal bool UseTopLevelInstructions => SystemAsInstructions && !SystemAsDeveloper;

    internal string MessageRole(string role) =>
        SystemAsDeveloper && role == "system" ? "developer" : role;
}
