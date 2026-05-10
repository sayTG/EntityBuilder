namespace EntityBuilder.Configuration;

public class RedisSettings
{
    public const string SectionName = "RedisSettings";
    public string? ConnectionString { get; set; }
}
