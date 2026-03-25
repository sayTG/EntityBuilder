namespace EntityBuilder.Models;

public class ReportEmailRequest
{
    public required string Sql { get; set; }
    public required string Token { get; set; }
    public required string RecipientEmail { get; set; }
    public required string DisplayName { get; set; }
    public string Subject { get; set; } = "Entity Builder Report";
    public Dictionary<string, string> TemplateValues { get; set; } = new();
    public Dictionary<string, string> DapperTemplateValues { get; set; } = new();
}
