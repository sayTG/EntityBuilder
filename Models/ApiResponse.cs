using System.Text.Json.Serialization;

namespace EntityBuilder.Models;

public class ApiResponse<T>
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("shortDescription")]
    public string? ShortDescription { get; set; }

    [JsonPropertyName("data")]
    public T? Data { get; set; }
}

public class LoginData
{
    [JsonPropertyName("firstName")]
    public string FirstName { get; set; } = "";

    [JsonPropertyName("lastName")]
    public string LastName { get; set; } = "";

    [JsonPropertyName("userName")]
    public string UserName { get; set; } = "";

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("token")]
    public string Token { get; set; } = "";

    [JsonPropertyName("tokenExpiration")]
    public string? TokenExpiration { get; set; }

    [JsonPropertyName("rolesDTOs")]
    public List<RoleDto> RolesDTOs { get; set; } = new();
}

public class RoleDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}
