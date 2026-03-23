namespace EntityBuilder.Configuration;

public class AuthSettings
{
    public const string SectionName = "AuthSettings";
    public List<UserCredential> Users { get; set; } = new();
}

public class UserCredential
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}
