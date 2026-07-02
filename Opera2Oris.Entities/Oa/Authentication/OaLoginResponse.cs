using System.Text.Json.Serialization;

namespace Opera2Oris.Entities;

public sealed class OaLoginResponse
{
    [JsonPropertyName("token")]
    public string Token { get; init; } = string.Empty;
}
