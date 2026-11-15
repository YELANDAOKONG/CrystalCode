using System.Text.Json;

namespace CrystalCode.Engine.Authentication;

internal sealed class AuthenticationEntryDocument
{
    public string? Integration { get; set; }

    public JsonElement? Data { get; set; }
}
