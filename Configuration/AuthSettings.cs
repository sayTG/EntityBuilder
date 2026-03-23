namespace EntityBuilder.Configuration;

public class AuthSettings
{
    public const string SectionName = "AuthSettings";
    public string BaseUrl { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public List<string> AllowedRoles { get; set; } = new();
}
