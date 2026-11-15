namespace CrystalCode.Authentication.ChatGpt;

/// <summary>
/// Fixed addresses and values of the Sign in with ChatGPT plan-usage flow.
/// </summary>
internal static class ChatGptEndpoints
{
    public const string IntegrationId = "chatgpt";
    public const string Issuer = "https://auth.openai.com";
    public const string AuthorizeUrl = "https://auth.openai.com/api/accounts/authorize";
    public const string TokenUrl = "https://auth.openai.com/api/accounts/oauth/token";
    public const string JwksUrl = "https://auth.openai.com/.well-known/jwks.json";
    public const string DiscoveryUrl = "https://auth.openai.com/.well-known/openid-configuration";
    public const string Resource = "https://api.openai.com/v1";
    public const string RegistrationClientId = "dynamic_agent_client";
    public const string AgentName = "Crystal Code";
    public const string Scope =
        "openid profile email offline_access resource.invoke chatgpt.tokens.use.direct";
    public const string PlanUsageScope = "chatgpt.tokens.use.direct";
    public const string CallbackPath = "/auth/callback";
    public const int PreferredCallbackPort = 1455;

    public static Uri InferenceBase { get; } = new(Resource + "/");
}
