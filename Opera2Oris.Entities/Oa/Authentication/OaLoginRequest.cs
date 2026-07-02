using System.Text.Json.Serialization;

namespace Opera2Oris.Entities;

public sealed class OaLoginRequest
{
    [JsonPropertyName("user")]
    public string? User { get; init; }

    [JsonPropertyName("password")]
    public string? Password { get; init; }

    [JsonPropertyName("language")]
    public string? Language { get; init; }

    [JsonPropertyName("databaseName")]
    public string? DatabaseName { get; init; }

    [JsonPropertyName("databaseUserName")]
    public string? DatabaseUserName { get; init; }

    [JsonPropertyName("databaseUserPassword")]
    public string? DatabaseUserPassword { get; init; }

    [JsonPropertyName("databaseIsLocal")]
    public bool? DatabaseIsLocal { get; init; }

    [JsonPropertyName("localDatabasePath")]
    public string? LocalDatabasePath { get; init; }

    [JsonPropertyName("sqlServer")]
    public string? SqlServer { get; init; }

    [JsonPropertyName("useWindowsAuthentication")]
    public bool? UseWindowsAuthentication { get; init; }
}
