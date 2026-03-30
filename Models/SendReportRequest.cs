namespace EntityBuilder.Models;

public class SendReportRequest
{
    public string Sql { get; set; } = "";
    public string? Subject { get; set; }
    public string? RecipientEmail { get; set; }
    public Dictionary<string, string>? TemplateValues { get; set; }
    public Dictionary<string, string>? DapperTemplateValues { get; set; }
}
