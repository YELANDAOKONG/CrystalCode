namespace CrystalCode.Engine.Configuration;

/// <summary>
/// Per-model switches for the responses protocol, for endpoints whose request
/// shape differs from the public API. A null value keeps the default for the
/// provider's type.
/// </summary>
/// <param name="SystemAsInstructions">
/// Send system text in <c>instructions</c> instead of as system message items.
/// </param>
/// <param name="IncludeEncryptedReasoning">
/// Ask for encrypted reasoning content so reasoning can be replayed.
/// </param>
/// <param name="OmitSampling">
/// Leave out <c>temperature</c>, <c>top_p</c>, and the output token cap.
/// </param>
/// <param name="SystemAsDeveloper">
/// Send system messages as developer messages in input, overriding <c>SystemAsInstructions</c>.
/// </param>
public sealed record ResponsesRequestSettings(
    bool? SystemAsInstructions = null,
    bool? IncludeEncryptedReasoning = null,
    bool? OmitSampling = null,
    bool? SystemAsDeveloper = null);
