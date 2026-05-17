namespace EntityBuilder.Configuration;

public class MessagingSettings
{
    public const string SectionName = "MessagingSettings";
    public string BaseUrl { get; set; } = string.Empty;
    public string ExternalApiKey { get; set; } = string.Empty;
}
