namespace EntityBuilder.Configuration;

public class DatabaseSettings
{
    public const string SectionName = "DatabaseSettings";
    public string ProviderType { get; set; } = "SqlServer";
}
